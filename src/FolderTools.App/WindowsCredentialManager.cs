using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FolderTools.App
{
    /// <summary>Generic credentials protected by Windows and scoped to the current Windows account/device.</summary>
    internal static class WindowsCredentialManager
    {
        private const int GenericCredential = 1;
        private const int PersistLocalMachine = 2;
        private const string MasterTarget = "FolderTools:MasterPassword:v1";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static void SaveMasterPassword(string password)
        {
            Write(MasterTarget, "FolderTools", password);
        }

        public static string? ReadMasterPassword()
        {
            return Read(MasterTarget)?.Secret;
        }

        public static void ForgetMasterPassword()
        {
            Delete(MasterTarget);
        }

        public static void SaveSftp(string destinationId, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(destinationId)) throw new ArgumentException("Не указан идентификатор SFTP-получателя.", nameof(destinationId));
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (password == null) throw new ArgumentNullException(nameof(password));
            Write(SftpTarget(destinationId), username, username + "\n" + password);
        }

        public static Tuple<string, string>? ReadSftp(string destinationId)
        {
            StoredCredential? credential = Read(SftpTarget(destinationId));
            if (credential == null) return null;
            int separator = credential.Secret.IndexOf('\n');
            if (separator < 1) return null;
            return Tuple.Create(credential.Secret.Substring(0, separator), credential.Secret.Substring(separator + 1));
        }

        public static void ForgetSftp(string destinationId)
        {
            Delete(SftpTarget(destinationId));
        }

        private static string SftpTarget(string id) => "FolderTools:SFTP:v1:" + id;

        private static void Write(string target, string username, string secret)
        {
            EnsureWindows();
            byte[] bytes = Utf8.GetBytes(secret);
            if (bytes.Length > 5120)
            {
                Array.Clear(bytes, 0, bytes.Length);
                throw new InvalidOperationException("Учётные данные превышают лимит Windows Credential Manager (5120 байт).");
            }

            IntPtr blob = IntPtr.Zero;
            try
            {
                blob = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
                if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = GenericCredential,
                    TargetName = target,
                    CredentialBlobSize = bytes.Length,
                    CredentialBlob = blob,
                    Persist = PersistLocalMachine,
                    UserName = username
                };
                if (!CredWriteW(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager не сохранил учётные данные.");
                }
            }
            finally
            {
                if (blob != IntPtr.Zero)
                {
                    for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
                    Marshal.FreeHGlobal(blob);
                }
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static StoredCredential? Read(string target)
        {
            EnsureWindows();
            IntPtr pointer;
            if (!CredReadW(target, GenericCredential, 0, out pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 1168) return null; // ERROR_NOT_FOUND
                throw new Win32Exception(error, "Windows Credential Manager не прочитал учётные данные.");
            }

            try
            {
                var credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                if (credential.CredentialBlobSize < 0 || credential.CredentialBlobSize > 5120)
                    throw new InvalidOperationException("Повреждена запись в Windows Credential Manager.");
                byte[] bytes = new byte[credential.CredentialBlobSize];
                if (bytes.Length > 0) Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                try { return new StoredCredential(credential.UserName ?? string.Empty, Utf8.GetString(bytes)); }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            finally { CredFree(pointer); }
        }

        private static void Delete(string target)
        {
            EnsureWindows();
            if (!CredDeleteW(target, GenericCredential, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1168) throw new Win32Exception(error, "Windows Credential Manager не удалил учётные данные.");
            }
        }

        private static void EnsureWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Windows Credential Manager доступен только в Windows.");
        }

        private sealed class StoredCredential
        {
            public StoredCredential(string username, string secret) { Username = username; Secret = secret; }
            public string Username { get; }
            public string Secret { get; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public int Flags;
            public int Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWriteW(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDeleteW(string target, int type, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = false)]
        private static extern void CredFree(IntPtr buffer);
    }
}
