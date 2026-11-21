using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace FolderTools.App
{
    /// <summary>
    /// Stores SFTP credentials per Windows user and destination. The username and
    /// password are protected together with DPAPI CurrentUser; neither is written
    /// to disk in plaintext. The file lives in the current user's LocalAppData so
    /// credentials from separate Windows accounts never overwrite one another.
    /// </summary>
    internal static class SftpCredentialStore
    {
        private const int FormatVersion = 1;
        private const string ApplicationPurpose = "FolderTools:SFTP:credentials:v1:";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static string StorePath
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "FolderTools", "sftp-credentials.dat");
            }
        }

        public static string CurrentUserSid
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    string sid = identity.User == null ? string.Empty : identity.User.Value;
                    if (string.IsNullOrWhiteSpace(sid))
                    {
                        throw new InvalidOperationException("Не удалось определить SID текущего пользователя Windows.");
                    }

                    return sid;
                }
            }
        }

        /// <summary>
        /// Saves credentials for this destination and this Windows SID only. Saving
        /// another destination, or running as another SID, does not replace this entry.
        /// </summary>
        public static void Save(string destinationId, string username, string password)
        {
            ValidateDestinationId(destinationId);
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("Не указано имя пользователя SFTP.", nameof(username));
            }

            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }

            string sid = CurrentUserSid;
            List<CredentialRecord> records = ReadRecords();
            records.RemoveAll(record =>
                string.Equals(record.DestinationId, destinationId, StringComparison.Ordinal)
                && string.Equals(record.UserSid, sid, StringComparison.OrdinalIgnoreCase));

            byte[] clear = Utf8.GetBytes(username + "\n" + password);
            byte[] encrypted;
            try
            {
                encrypted = ProtectedData.Protect(clear, Entropy(destinationId, sid), DataProtectionScope.CurrentUser);
            }
            finally
            {
                Array.Clear(clear, 0, clear.Length);
            }

            records.Add(new CredentialRecord(destinationId, sid, Convert.ToBase64String(encrypted)));
            Array.Clear(encrypted, 0, encrypted.Length);
            WriteRecords(records);
        }

        /// <summary>
        /// Returns NotFound when no record exists, Available when DPAPI can decrypt
        /// this user's record, and NeedsReentry when a record exists but is bound to
        /// another SID/device DPAPI context or has been damaged.
        /// </summary>
        public static SftpCredentialLookup Get(string destinationId)
        {
            ValidateDestinationId(destinationId);
            string sid = CurrentUserSid;
            List<CredentialRecord> records = ReadRecords();
            CredentialRecord? record = records.Find(item =>
                string.Equals(item.DestinationId, destinationId, StringComparison.Ordinal)
                && string.Equals(item.UserSid, sid, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                bool otherSidExists = records.Exists(item =>
                    string.Equals(item.DestinationId, destinationId, StringComparison.Ordinal));
                return otherSidExists
                    ? SftpCredentialLookup.NeedsReentry()
                    : SftpCredentialLookup.NotFound();
            }

            try
            {
                byte[] encrypted = Convert.FromBase64String(record.ProtectedCredentials);
                byte[] clear = ProtectedData.Unprotect(encrypted, Entropy(destinationId, sid), DataProtectionScope.CurrentUser);
                Array.Clear(encrypted, 0, encrypted.Length);
                try
                {
                    string value = Utf8.GetString(clear);
                    int separator = value.IndexOf('\n');
                    if (separator < 1)
                    {
                        return SftpCredentialLookup.NeedsReentry();
                    }

                    return SftpCredentialLookup.Available(value.Substring(0, separator), value.Substring(separator + 1));
                }
                finally
                {
                    Array.Clear(clear, 0, clear.Length);
                }
            }
            catch (CryptographicException)
            {
                return SftpCredentialLookup.NeedsReentry();
            }
            catch (FormatException)
            {
                return SftpCredentialLookup.NeedsReentry();
            }
        }

        private static byte[] Entropy(string destinationId, string sid)
        {
            return Encoding.UTF8.GetBytes(ApplicationPurpose + sid + ":" + destinationId);
        }

        private static void ValidateDestinationId(string destinationId)
        {
            if (string.IsNullOrWhiteSpace(destinationId))
            {
                throw new ArgumentException("Не указан идентификатор папки назначения.", nameof(destinationId));
            }
        }

        private static List<CredentialRecord> ReadRecords()
        {
            string path = StorePath;
            if (!File.Exists(path))
            {
                return new List<CredentialRecord>();
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                int version = reader.ReadInt32();
                if (version != FormatVersion)
                {
                    throw new InvalidDataException("Неизвестная версия хранилища паролей SFTP.");
                }

                int count = reader.ReadInt32();
                if (count < 0 || count > 10000)
                {
                    throw new InvalidDataException("Повреждено хранилище паролей SFTP.");
                }

                var records = new List<CredentialRecord>(count);
                for (int i = 0; i < count; i++)
                {
                    records.Add(new CredentialRecord(
                        reader.ReadString(),
                        reader.ReadString(),
                        reader.ReadString()));
                }

                if (stream.Position != stream.Length)
                {
                    throw new InvalidDataException("В хранилище паролей SFTP есть лишние или повреждённые данные.");
                }

                return records;
            }
        }

        private static void WriteRecords(List<CredentialRecord> records)
        {
            string path = StorePath;
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Не удалось определить папку хранилища паролей SFTP.");
            }

            Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(FormatVersion);
                writer.Write(records.Count);
                foreach (CredentialRecord record in records)
                {
                    writer.Write(record.DestinationId);
                    writer.Write(record.UserSid);
                    writer.Write(record.ProtectedCredentials);
                }

                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                string backup = path + ".bak";
                File.Replace(temp, path, backup, true);
                TryDelete(backup);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A stale backup is harmless; the committed store is already in place.
            }
        }

        private sealed class CredentialRecord
        {
            public CredentialRecord(string destinationId, string userSid, string protectedCredentials)
            {
                DestinationId = destinationId;
                UserSid = userSid;
                ProtectedCredentials = protectedCredentials;
            }

            public string DestinationId { get; }
            public string UserSid { get; }
            public string ProtectedCredentials { get; }
        }
    }

    internal enum SftpCredentialState
    {
        NotFound,
        Available,
        NeedsReentry
    }

    internal sealed class SftpCredentialLookup
    {
        private SftpCredentialLookup(SftpCredentialState state, string? username, string? password)
        {
            State = state;
            Username = username;
            Password = password;
        }

        public SftpCredentialState State { get; }
        public string? Username { get; }
        public string? Password { get; }

        public static SftpCredentialLookup NotFound() => new SftpCredentialLookup(SftpCredentialState.NotFound, null, null);
        public static SftpCredentialLookup NeedsReentry() => new SftpCredentialLookup(SftpCredentialState.NeedsReentry, null, null);
        public static SftpCredentialLookup Available(string username, string password) => new SftpCredentialLookup(SftpCredentialState.Available, username, password);
    }
}
