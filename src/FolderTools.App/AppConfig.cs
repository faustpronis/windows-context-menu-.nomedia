using System;
using System.IO;
using Microsoft.Win32;

namespace FolderTools.App
{
    /// <summary>
    /// Settings stored in HKCU\Software\FolderTools so the tool can be configured
    /// without administrator rights.
    /// </summary>
    internal static class AppConfig
    {
        private const string KeyPath = @"Software\FolderTools";
        private const string DeskEditValue = "DeskEditPath";
        private const string ShowSuccessValue = "ShowSuccess";

        public const string DeskEditDownloadPage = "https://www.sordum.org/10084/desktop-ini-editor-v1-2/";

        /// <summary>
        /// Confirmation dialogs are off by default: the action is started from a
        /// context menu and the result is visible in the folder itself.
        /// </summary>
        public static bool ShowSuccess
        {
            get { return ReadDword(ShowSuccessValue) == 1; }
        }

        public static string? DeskEditPath
        {
            get { return ReadString(DeskEditValue); }
        }

        /// <summary>Returns the configured editor, or one found next to the tool / in C:\Tools.</summary>
        public static string? ResolveDeskEdit()
        {
            string? configured = DeskEditPath;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string name in new[] { "DeskEdit_x64.exe", "DeskEdit.exe" })
            {
                string candidate = Path.Combine(baseDir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            foreach (string candidate in new[]
            {
                @"C:\Tools\DeskEdit\DeskEdit_x64.exe",
                @"C:\Tools\DeskEdit\DeskEdit.exe"
            })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string? ReadString(string name)
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                return key?.GetValue(name) as string;
            }
        }

        private static int ReadDword(string name)
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                object? value = key?.GetValue(name);
                return value is int i ? i : 0;
            }
        }
    }
}
