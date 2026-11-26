using System;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public sealed class PortableCredentialCryptoTests
    {
        [Fact]
        public void EncryptDecrypt_RoundTripsUtf8Secret()
        {
            const string password = "Очень-сложный мастер пароль 123!";
            const string secret = "pāssword/пароль\nline2";

            string encrypted = PortableCredentialCrypto.Encrypt(secret, password);

            Assert.StartsWith("ft1$600000$", encrypted);
            Assert.DoesNotContain(secret, encrypted, StringComparison.Ordinal);
            Assert.Equal(secret, PortableCredentialCrypto.Decrypt(encrypted, password));
        }

        [Fact]
        public void Decrypt_RejectsWrongMasterPassword()
        {
            string encrypted = PortableCredentialCrypto.Encrypt("secret", "correct horse battery staple");

            Assert.Throws<System.Security.Cryptography.CryptographicException>(
                () => PortableCredentialCrypto.Decrypt(encrypted, "wrong password"));
        }

        [Fact]
        public void Decrypt_RejectsBlankMasterPasswordAsCredentialError()
        {
            string encrypted = PortableCredentialCrypto.Encrypt("secret", "correct horse battery staple");

            Assert.Throws<System.Security.Cryptography.CryptographicException>(
                () => PortableCredentialCrypto.Decrypt(encrypted, string.Empty));
        }

        [Fact]
        public void Decrypt_RejectsTamperedPayload()
        {
            string encrypted = PortableCredentialCrypto.Encrypt("secret", "correct horse battery staple");
            string[] fields = encrypted.Split('$');
            char first = fields[5][0];
            fields[5] = (first == 'A' ? "B" : "A") + fields[5].Substring(1);

            Assert.Throws<System.Security.Cryptography.CryptographicException>(
                () => PortableCredentialCrypto.Decrypt(string.Join("$", fields), "correct horse battery staple"));
        }

        [Fact]
        public void Encrypt_RejectsBlankMasterPassword()
        {
            Assert.Throws<ArgumentException>(() => PortableCredentialCrypto.Encrypt("secret", "  "));
        }
    }
}
