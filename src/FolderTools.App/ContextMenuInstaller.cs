using System;
using System.IO;
using FolderTools.Core;
using Microsoft.Win32;

namespace FolderTools.App
{
    /// <summary>
    /// Registers the folder context-menu entry (a cascading menu with the tool's
    /// commands) under HKEY_CURRENT_USER, so no administrator rights are needed.
    /// Layout follows the documented ExtendedSubCommandsKey form:
    /// shell\FolderTools\ExtendedSubCommandsKey\Shell\&lt;verb&gt;\command.
    /// </summary>
    internal static class ContextMenuInstaller
    {
        private const string ConfigKey = @"Software\FolderTools";

        private static string ExecutablePath
        {
            get { return ProductInfo.ExecutablePath; }
        }

        public static void Install()
        {
            string exe = ExecutablePath;

            foreach (RegistryEntry entry in MenuLayout.Build(exe))
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(entry.KeyPath))
                {
                    key.SetValue(entry.Name ?? string.Empty, entry.Value);
                }
            }

            using (RegistryKey config = Registry.CurrentUser.CreateSubKey(ConfigKey))
            {
                config.SetValue("InstalledExe", exe);
            }
        }

        public static void Uninstall()
        {
            foreach (string target in MenuLayout.Targets)
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    MenuLayout.ClassesRoot + "\\" + target + @"\shell\" + MenuLayout.MenuKey, false);
            }

            Registry.CurrentUser.DeleteSubKeyTree(
                MenuLayout.ClassesRoot + "\\" + MenuLayout.VerbStore, false);
        }

        public static void SetDeskEditPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Не указан путь к DeskEdit.exe.");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Файл не найден: " + path);
            }

            using (RegistryKey config = Registry.CurrentUser.CreateSubKey(ConfigKey))
            {
                config.SetValue("DeskEditPath", path);
            }
        }
    }
}
