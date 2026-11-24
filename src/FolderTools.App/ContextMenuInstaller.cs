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

        /// <summary>Settings file next to the exe, so it travels with the program.</summary>
        public static string SettingsPath
        {
            get { return SettingsStore.DefaultPath(AppDomain.CurrentDomain.BaseDirectory); }
        }

        public static FolderToolsSettings LoadSettings()
        {
            return SettingsStore.Load(SettingsPath);
        }

        public static void SaveSettings(FolderToolsSettings settings)
        {
            SettingsStore.Save(SettingsPath, settings);
            RefreshMenu();
        }

        public static void Install()
        {
            CleanupLegacyRootDlls();
            string exe = ExecutablePath;

            foreach (RegistryEntry entry in MenuLayout.Build(exe))
            {
                Write(entry);
            }

            foreach (RegistryEntry entry in MenuLayout.BuildShellNew(exe))
            {
                Write(entry);
            }

            using (RegistryKey config = Registry.CurrentUser.CreateSubKey(ConfigKey))
            {
                config.SetValue("InstalledExe", exe);
            }

            RefreshMenu();
        }

        internal static string[] CleanupLegacyRootDlls()
        {
            var undeleted = new System.Collections.Generic.List<string>();
            foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    undeleted.Add(Path.GetFileName(file));
                }
                catch (UnauthorizedAccessException)
                {
                    undeleted.Add(Path.GetFileName(file));
                }
            }
            return undeleted.ToArray();
        }

        /// <summary>
        /// Rewrites the destination verbs from the current settings. Called after every
        /// settings change so the menu never disagrees with what the GUI shows.
        /// </summary>
        public static void RefreshMenu()
        {
            string exe = ExecutablePath;
            foreach (string target in MenuLayout.FileTargets)
            {
                string shell = MenuLayout.ClassesRoot + "\\" + target + @"\shell";
                if (target == "*")
                {
                    Registry.CurrentUser.DeleteSubKeyTree(shell + "\\" + DestinationMenuLayout.FileRoot, false);
                }
                if (target == "Directory")
                {
                    string folderActions = shell + "\\" + DestinationMenuLayout.FileRoot + @"\ExtendedSubCommandsKey\Shell";
                    Registry.CurrentUser.DeleteSubKeyTree(folderActions + @"\03copyto", false);
                    Registry.CurrentUser.DeleteSubKeyTree(folderActions + @"\04moveto", false);
                }
            }

            ClearLegacyStandaloneRoots();
            ClearStores();
            foreach (RegistryEntry entry in DestinationMenuLayout.Build(
                exe,
                LoadSettings(),
                AppDomain.CurrentDomain.BaseDirectory))
            {
                Write(entry);
            }
        }

        public static void Uninstall()
        {
            foreach (string target in MenuLayout.Targets)
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    MenuLayout.ClassesRoot + "\\" + target + @"\shell\" + MenuLayout.MenuKey, false);
            }

            foreach (RegistryEntry entry in MenuLayout.BuildShellNew(ExecutablePath))
            {
                Registry.CurrentUser.DeleteSubKeyTree(entry.KeyPath, false);
            }

            foreach (string target in MenuLayout.FileTargets)
            {
                string shell = MenuLayout.ClassesRoot + "\\" + target + @"\shell";
                if (target == "*")
                {
                    Registry.CurrentUser.DeleteSubKeyTree(shell + "\\" + DestinationMenuLayout.FileRoot, false);
                }
            }

            ClearLegacyStandaloneRoots();
            Registry.CurrentUser.DeleteSubKeyTree(
                MenuLayout.ClassesRoot + "\\" + MenuLayout.VerbStore, false);

            ClearStores();
        }

        /// <summary>
        /// Drops every shared verb store this tool owns. They live outside the per-target
        /// shell keys, so a renamed or removed destination would otherwise linger.
        /// </summary>
        private static void ClearStores()
        {
            using (RegistryKey classes = Registry.CurrentUser.OpenSubKey(MenuLayout.ClassesRoot))
            {
                if (classes == null)
                {
                    return;
                }

                foreach (string name in classes.GetSubKeyNames())
                {
                    if (DestinationMenuLayout.IsOwnedStore(name))
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(
                            MenuLayout.ClassesRoot + "\\" + name, false);
                    }
                }
            }
        }

        private static void ClearLegacyStandaloneRoots()
        {
            string[] targets = { "*", "Directory", @"Directory\Background", "Drive" };
            string[] oldRootNames =
            {
                DestinationMenuLayout.CopyRoot,
                DestinationMenuLayout.MoveRoot,
                "03copyto",
                "04moveto"
            };
            foreach (string target in targets)
            {
                string shell = MenuLayout.ClassesRoot + "\\" + target + @"\shell";
                foreach (string rootName in oldRootNames)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(shell + "\\" + rootName, false);
                }
            }
        }

        private static void Write(RegistryEntry entry)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(entry.KeyPath))
            {
                key.SetValue(entry.Name ?? string.Empty, entry.Value);
            }
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

            // The settings file is the single source of truth now; the registry copy
            // only existed for older builds.
            AppConfig.SetDeskEditPath(path);
        }
    }
}
