using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using FolderTools.Core;

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

        /// <summary>Reads this computer's OS credential cache, migrating a legacy DPAPI entry when present.</summary>
        public static SftpCredentialLookup GetCached(Destination destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            Tuple<string, string>? local = WindowsCredentialManager.ReadSftp(destination.Id);
            if (local != null) return SftpCredentialLookup.Available(local.Item1, local.Item2);
            SftpCredentialLookup legacy = Get(destination.Id);
            if (legacy.State == SftpCredentialState.Available && legacy.Username != null && legacy.Password != null)
            {
                WindowsCredentialManager.SaveSftp(destination.Id, legacy.Username, legacy.Password);
                return legacy;
            }
            return !string.IsNullOrWhiteSpace(destination.SftpPasswordEncrypted)
                ? SftpCredentialLookup.NeedsReentry()
                : legacy;
        }

        /// <summary>Loads cached credentials or unlocks the portable payload with the local master password.</summary>
        public static SftpCredentialLookup GetForUse(Destination destination, string? hint, IWin32Window? owner)
        {
            SftpCredentialLookup cached = GetCached(destination);
            if (cached.State == SftpCredentialState.Available) return cached;
            if (string.IsNullOrWhiteSpace(destination.SftpPasswordEncrypted)) return cached;
            string? master = WindowsCredentialManager.ReadMasterPassword();
            if (master == null)
            {
                master = MasterPasswordEntryForm.Prompt(owner, hint);
                if (master == null) return SftpCredentialLookup.NeedsReentry();
            }
            try { return UnlockAndCache(destination, master); }
            catch (System.Security.Cryptography.CryptographicException)
            {
                string? retry = MasterPasswordEntryForm.Prompt(owner, hint, "Не удалось расшифровать. Проверьте мастер-пароль и попробуйте снова.");
                if (retry == null) return SftpCredentialLookup.NeedsReentry();
                try { return UnlockAndCache(destination, retry); }
                catch (System.Security.Cryptography.CryptographicException) { return SftpCredentialLookup.NeedsReentry(); }
            }
        }

        private static SftpCredentialLookup UnlockAndCache(Destination destination, string master)
        {
            string password = PortableCredentialCrypto.Decrypt(destination.SftpPasswordEncrypted!, master);
            string username = destination.SftpUsername ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username)) return SftpCredentialLookup.NeedsReentry();
            WindowsCredentialManager.SaveSftp(destination.Id, username, password);
            WindowsCredentialManager.SaveMasterPassword(master);
            return SftpCredentialLookup.Available(username, password);
        }

        /// <summary>Writes both portable authenticated ciphertext and the local OS cache.</summary>
        public static void SavePortable(Destination destination, string username, string password, string masterPassword)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.SftpUsername = username;
            destination.SftpPasswordEncrypted = PortableCredentialCrypto.Encrypt(password, masterPassword);
        }

        /// <summary>Creates a local credential cache after the portable settings have been committed.</summary>
        public static void CachePortable(Destination destination, string masterPassword)
        {
            string password = PortableCredentialCrypto.Decrypt(destination.SftpPasswordEncrypted!, masterPassword);
            string username = destination.SftpUsername ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("В portable записи не указано имя пользователя SFTP.");
            WindowsCredentialManager.SaveSftp(destination.Id, username, password);
        }

        public static void ForgetLocalCredential(string destinationId)
        {
            WindowsCredentialManager.ForgetSftp(destinationId);
            string sid = CurrentUserSid;
            List<CredentialRecord> records = ReadRecords();
            int removed = records.RemoveAll(record =>
                string.Equals(record.DestinationId, destinationId, StringComparison.Ordinal) &&
                string.Equals(record.UserSid, sid, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) WriteRecords(records);
        }

        public static string? GetLocalMasterPassword() => WindowsCredentialManager.ReadMasterPassword();
        public static void SaveLocalMasterPassword(string password) => WindowsCredentialManager.SaveMasterPassword(password);

        /// <summary>Forgets cached secrets on this Windows account only; portable ciphertext is not changed.</summary>
        public static void ForgetLocal(IEnumerable<Destination> destinations)
        {
            foreach (Destination destination in (destinations ?? Enumerable.Empty<Destination>()).Where(d => d.Type == DestinationType.Sftp))
                WindowsCredentialManager.ForgetSftp(destination.Id);
            WindowsCredentialManager.ForgetMasterPassword();
            foreach (string path in new[] { StorePath, StorePath + ".tmp", StorePath + ".bak" })
                if (File.Exists(path)) File.Delete(path);
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

        /// <summary>Copies credentials locally when deriving a new destination from a saved one.</summary>
        public static bool Copy(string sourceDestinationId, string targetDestinationId)
        {
            SftpCredentialLookup source = Get(sourceDestinationId);
            if (source.State != SftpCredentialState.Available || source.Username == null || source.Password == null)
            {
                return false;
            }

            Save(targetDestinationId, source.Username, source.Password);
            return true;
        }

        /// <summary>Copies portable credentials to a new SFTP folder and duplicates an available local cache.</summary>
        public static bool Copy(Destination source, Destination target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.SftpUsername = source.SftpUsername;
            target.SftpPasswordEncrypted = source.SftpPasswordEncrypted;
            SftpCredentialLookup cached = GetCached(source);
            if (cached.State == SftpCredentialState.Available && cached.Username != null && cached.Password != null)
            {
                WindowsCredentialManager.SaveSftp(target.Id, cached.Username, cached.Password);
                return true;
            }
            return !string.IsNullOrWhiteSpace(target.SftpPasswordEncrypted);
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
