using System;
using System.Security.Cryptography;
using System.Text;

namespace FolderTools.Core
{
    /// <summary>
    /// Portable password encryption for secrets embedded in the synchronized settings file.
    /// Uses PBKDF2-HMAC-SHA256 and AES-256-CBC with encrypt-then-MAC (HMAC-SHA256).
    /// The master password is never part of the serialized payload.
    /// </summary>
    public static class PortableCredentialCrypto
    {
        private const int FormatVersion = 1;
        private const int Iterations = 600000;
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const int MacSize = 32;
        private const int KeySize = 64;
        private const string Purpose = "FolderTools portable SFTP credential v1";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static string Encrypt(string plaintext, string masterPassword)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            ValidateMasterPassword(masterPassword);

            byte[] salt = new byte[SaltSize];
            byte[] iv = new byte[IvSize];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
                random.GetBytes(iv);
            }

            byte[] key = DeriveKey(masterPassword, salt, Iterations);
            byte[] clear = Utf8.GetBytes(plaintext);
            byte[] encrypted;
            byte[] mac;
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    byte[] encryptionKey = Slice(key, 0, 32);
                    try { aes.Key = encryptionKey; }
                    finally { Array.Clear(encryptionKey, 0, encryptionKey.Length); }
                    aes.IV = iv;
                    using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    {
                        encrypted = encryptor.TransformFinalBlock(clear, 0, clear.Length);
                    }
                }

                mac = ComputeMac(key, salt, iv, encrypted, Iterations);
                return string.Join("$", "ft" + FormatVersion, Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToBase64String(salt), Convert.ToBase64String(iv), Convert.ToBase64String(encrypted), Convert.ToBase64String(mac));
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(clear, 0, clear.Length);
            }
        }

        public static string Decrypt(string payload, string masterPassword)
        {
            if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Не задан зашифрованный пароль.", nameof(payload));
            if (string.IsNullOrWhiteSpace(masterPassword)) throw new CryptographicException("Введите мастер-пароль.");
            ValidateMasterPassword(masterPassword);

            string[] parts = payload.Split('$');
            if (parts.Length != 6 || parts[0] != "ft" + FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new CryptographicException("Неизвестный формат переносимых учётных данных.");

            int iterations;
            if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations)
                || iterations < 100000 || iterations > 2000000)
                throw new CryptographicException("Некорректные параметры derivation key.");

            byte[] salt, iv, encrypted, suppliedMac;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                iv = Convert.FromBase64String(parts[3]);
                encrypted = Convert.FromBase64String(parts[4]);
                suppliedMac = Convert.FromBase64String(parts[5]);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("Повреждённый формат переносимых учётных данных.", ex);
            }
            if (salt.Length != SaltSize || iv.Length != IvSize || encrypted.Length == 0 || encrypted.Length % IvSize != 0 || suppliedMac.Length != MacSize)
                throw new CryptographicException("Повреждённый формат переносимых учётных данных.");

            byte[] key = DeriveKey(masterPassword, salt, iterations);
            byte[] expectedMac = ComputeMac(key, salt, iv, encrypted, iterations);
            try
            {
                if (!FixedTimeEquals(expectedMac, suppliedMac))
                    throw new CryptographicException("Неверный мастер-пароль или повреждённые учётные данные.");

                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    byte[] encryptionKey = Slice(key, 0, 32);
                    try { aes.Key = encryptionKey; }
                    finally { Array.Clear(encryptionKey, 0, encryptionKey.Length); }
                    aes.IV = iv;
                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        byte[] clear = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                        try { return Utf8.GetString(clear); }
                        finally { Array.Clear(clear, 0, clear.Length); }
                    }
                }
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(expectedMac, 0, expectedMac.Length);
                Array.Clear(suppliedMac, 0, suppliedMac.Length);
            }
        }

        private static byte[] DeriveKey(string password, byte[] salt, int iterations)
        {
            byte[] passwordBytes = Utf8.GetBytes(password);
            byte[] result = new byte[KeySize];
            try
            {
                using (var hmac = new HMACSHA256(passwordBytes))
                {
                    DeriveBlock(hmac, salt, iterations, 1, result, 0);
                    DeriveBlock(hmac, salt, iterations, 2, result, 32);
                }
                return result;
            }
            catch
            {
                Array.Clear(result, 0, result.Length);
                throw;
            }
            finally { Array.Clear(passwordBytes, 0, passwordBytes.Length); }
        }

        private static void DeriveBlock(HMACSHA256 hmac, byte[] salt, int iterations, int blockNumber, byte[] output, int outputOffset)
        {
            byte[] saltBlock = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, saltBlock, 0, salt.Length);
            saltBlock[salt.Length] = (byte)(blockNumber >> 24);
            saltBlock[salt.Length + 1] = (byte)(blockNumber >> 16);
            saltBlock[salt.Length + 2] = (byte)(blockNumber >> 8);
            saltBlock[salt.Length + 3] = (byte)blockNumber;
            byte[] u = hmac.ComputeHash(saltBlock);
            byte[] accumulator = (byte[])u.Clone();
            try
            {
                for (int round = 1; round < iterations; round++)
                {
                    byte[] next = hmac.ComputeHash(u);
                    Array.Clear(u, 0, u.Length);
                    u = next;
                    for (int i = 0; i < accumulator.Length; i++) accumulator[i] ^= u[i];
                }
                Buffer.BlockCopy(accumulator, 0, output, outputOffset, accumulator.Length);
            }
            finally
            {
                Array.Clear(saltBlock, 0, saltBlock.Length);
                Array.Clear(u, 0, u.Length);
                Array.Clear(accumulator, 0, accumulator.Length);
            }
        }

        private static byte[] ComputeMac(byte[] key, byte[] salt, byte[] iv, byte[] ciphertext, int iterations)
        {
            byte[] prefix = Encoding.ASCII.GetBytes(Purpose + "$ft" + FormatVersion + "$" + iterations.ToString(System.Globalization.CultureInfo.InvariantCulture) + "$");
            byte[] data = new byte[prefix.Length + salt.Length + iv.Length + ciphertext.Length];
            Buffer.BlockCopy(prefix, 0, data, 0, prefix.Length);
            Buffer.BlockCopy(salt, 0, data, prefix.Length, salt.Length);
            Buffer.BlockCopy(iv, 0, data, prefix.Length + salt.Length, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, data, prefix.Length + salt.Length + iv.Length, ciphertext.Length);
            byte[] macKey = Slice(key, 32, 32);
            try
            {
                using (var hmac = new HMACSHA256(macKey)) return hmac.ComputeHash(data);
            }
            finally
            {
                Array.Clear(data, 0, data.Length);
                Array.Clear(macKey, 0, macKey.Length);
            }
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(source, offset, result, 0, count);
            return result;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        private static void ValidateMasterPassword(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Мастер-пароль не может быть пустым.", nameof(value));
        }
    }
}
