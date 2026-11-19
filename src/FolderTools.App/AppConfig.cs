using System;
using System.IO;
using FolderTools.Core;
using Microsoft.Win32;

namespace FolderTools.App
{
    /// <summary>
    /// Tool-wide options. They live in the settings file next to the executable, so
    /// the whole installation stays one portable folder.
    ///
    /// Older builds kept DeskEditPath and ShowSuccess in HKCU\Software\FolderTools.
    /// Those values are still read as a fallback, so an upgrade does not lose the
    /// configured editor.
    /// </summary>
    internal static class AppConfig
    {
        private const string LegacyKeyPath = @"Software\FolderTools";
        private const string LegacyDeskEditValue = "DeskEditPath";
        private const string LegacyShowSuccessValue = "ShowSuccess";

        public const string DeskEditDownloadPage = "https://www.sordum.org/10084/desktop-ini-editor-v1-2/";

        private static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

        private static FolderToolsSettings Read()
        {
            return SettingsStore.Load(SettingsStore.DefaultPath(BaseDirectory));
        }

        private static void Write(Action<FolderToolsSettings> update)
        {
            FolderToolsSettings settings = Read();
            update(settings);
            SettingsStore.Save(SettingsStore.DefaultPath(BaseDirectory), settings);
        }

        /// <summary>
        /// Confirmation dialogs are off by default: the action is started from a
        /// context menu and the result is visible in the folder itself.
        /// </summary>
        public static bool ShowSuccess
        {
            get
            {
                FolderToolsSettings settings = Read();
                return settings.ShowSuccess || ReadLegacyDword(LegacyShowSuccessValue) == 1;
            }
        }

        public static void SetShowSuccess(bool value)
        {
            Write(s => s.ShowSuccess = value);
        }

        public static string? DeskEditPath
        {
            get
            {
                string stored = Read().DeskEditPath ?? ReadLegacyString(LegacyDeskEditValue) ?? string.Empty;
                return string.IsNullOrWhiteSpace(stored) ? null : AppPaths.Resolve(BaseDirectory, stored);
            }
        }

        public static void SetDeskEditPath(string? path)
        {
            string stored = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AppPaths.ToStored(BaseDirectory, path);
            Write(s => s.DeskEditPath = stored);
        }

        /// <summary>Returns the configured editor, or one found next to the tool / in C:\Tools.</summary>
        public static string? ResolveDeskEdit()
        {
            string? configured = DeskEditPath;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            foreach (string name in new[] { "DeskEdit_x64.exe", "DeskEdit.exe" })
            {
                string candidate = Path.Combine(BaseDirectory, name);
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

        private static string? ReadLegacyString(string name)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(LegacyKeyPath))
                {
                    return key?.GetValue(name) as string;
                }
            }
            catch (Exception)
            {
                // The registry is only a migration source; a locked key must not stop
                // the tool from starting.
                return null;
            }
        }

        private static int ReadLegacyDword(string name)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(LegacyKeyPath))
                {
                    object? value = key?.GetValue(name);
                    return value is int i ? i : 0;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
