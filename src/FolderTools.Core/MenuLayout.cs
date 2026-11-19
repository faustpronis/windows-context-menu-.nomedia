using System;
using System.Collections.Generic;

namespace FolderTools.Core
{
    /// <summary>One registry value to write: <c>KeyPath</c>, value name (null = default) and data.</summary>
    public sealed class RegistryEntry
    {
        public RegistryEntry(string keyPath, string? name, string value)
        {
            KeyPath = keyPath;
            Name = name;
            Value = value;
        }

        public string KeyPath { get; }

        /// <summary>Value name, or null for the key's default value.</summary>
        public string? Name { get; }

        public string Value { get; }
    }

    /// <summary>
    /// Describes the folder context-menu registration as plain data, so the layout
    /// can be tested without touching the registry.
    ///
    /// The cascading submenu is registered in both forms the shell accepts: an
    /// <c>ExtendedSubCommandsKey</c> value pointing at a shared verb store (what
    /// context-menu tooling reads) and an <c>ExtendedSubCommandsKey</c> subkey
    /// holding the verbs inline (what the Win32 documentation describes). Both
    /// resolve to the same two commands.
    /// </summary>
    public static class MenuLayout
    {
        public const string MenuKey = "FolderTools";
        public const string ClassesRoot = @"Software\Classes";

        /// <summary>Shared verb store referenced by the <c>ExtendedSubCommandsKey</c> value.</summary>
        public const string VerbStore = "FolderTools.Menu";

        /// <summary>Shell objects the folder menu is attached to.</summary>
        public static readonly string[] Targets =
        {
            @"Directory\Background", // empty space inside a folder
            @"Directory",            // a folder icon
            @"Drive"                 // a drive root
        };

        /// <summary>
        /// Shell objects the copy/move verbs are attached to. "*" covers every file
        /// type, so a new verb does not have to be registered per extension.
        /// </summary>
        public static readonly string[] FileTargets =
        {
            @"*",
            @"Directory"
        };

        public static IReadOnlyList<RegistryEntry> Build(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException("Executable path must not be empty.", nameof(exePath));
            }

            var entries = new List<RegistryEntry>();
            foreach (string target in Targets)
            {
                string parent = ClassesRoot + "\\" + target + @"\shell\" + MenuKey;
                entries.Add(new RegistryEntry(parent, "MUIVerb", "FolderTools"));
                entries.Add(new RegistryEntry(parent, "Icon", "\"" + exePath + "\",0"));
                entries.Add(new RegistryEntry(parent, "ExtendedSubCommandsKey", VerbStore));

                AddVerbs(entries, parent + @"\ExtendedSubCommandsKey\Shell", exePath);
            }

            AddVerbs(entries, ClassesRoot + "\\" + VerbStore + @"\shell", exePath);
            return entries;
        }

        /// <summary>
        /// %V is the folder for a folder icon and for the empty area inside it;
        /// %1 is empty for background clicks, so it cannot be used here.
        /// </summary>
        public static string BuildCommand(string exePath, string verb)
        {
            return "\"" + exePath + "\" " + verb + " \"%V\"";
        }

        /// <summary>
        /// Command for a verb invoked on a file. The shell runs a legacy verb once per
        /// selected item, so %1 always names one file and routing by extension stays
        /// exact even for a large mixed selection.
        /// </summary>
        public static string BuildFilesCommand(string exePath, string verb, string argument)
        {
            return "\"" + exePath + "\" " + verb + " \"" + argument + "\" \"%1\"";
        }

        private static void AddVerbs(List<RegistryEntry> entries, string shellPath, string exePath)
        {
            entries.Add(new RegistryEntry(shellPath + @"\01nomedia", "MUIVerb", "Создать .nomedia"));
            entries.Add(new RegistryEntry(shellPath + @"\01nomedia\command", null, BuildCommand(exePath, "nomedia")));
            entries.Add(new RegistryEntry(shellPath + @"\02deskedit", "MUIVerb", "desktop.ini (DeskEdit)"));
            entries.Add(new RegistryEntry(shellPath + @"\02deskedit\command", null, BuildCommand(exePath, "deskedit")));
            entries.Add(new RegistryEntry(shellPath + @"\90settings", "MUIVerb", "Настройки…"));
            entries.Add(new RegistryEntry(shellPath + @"\90settings\command", null, "\"" + exePath + "\" settings"));
        }
    }
}
