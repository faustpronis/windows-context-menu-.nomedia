using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FolderTools.Core
{
    /// <summary>
    /// Builds the registry layout for the copy/move verbs.
    ///
    /// The verbs are static, but each one carries an <c>AppliesTo</c> filter written in
    /// Advanced Query Syntax, so Explorer hides it unless the selected item matches the
    /// configured extension patterns. AQS supports '*' and '?' wildcards, which map
    /// directly onto the user's '*' and '!' syntax, so no shell extension is needed.
    ///
    /// Destinations that target folders are registered under the Directory target
    /// instead, because a folder has no file name extension to filter on. Destinations
    /// grouped into a category become one submenu, which keeps the root menu short.
    ///
    /// Each submenu is written in both forms the shell accepts, exactly like the main
    /// FolderTools menu: an <c>ExtendedSubCommandsKey</c> value naming a verb store
    /// under HKEY_CLASSES_ROOT, and the same verbs inline under an
    /// <c>ExtendedSubCommandsKey</c> subkey.
    /// </summary>
    public static class DestinationMenuLayout
    {
        public const string CopyRoot = "01copyto";
        public const string MoveRoot = "02moveto";

        /// <summary>
        /// Registry entries for every configured destination, for both actions.
        /// A destination with no usable patterns is skipped, so a folder prepared in
        /// the GUI but not yet enabled stays out of the menu.
        /// </summary>
        public static IReadOnlyList<RegistryEntry> Build(string exePath, FolderToolsSettings settings)
        {
            return Build(exePath, settings, PathUtil.GetDirectoryName(exePath));
        }

        /// <summary>
        /// Registry entries for every configured destination, resolving per-entry icons
        /// against <paramref name="baseDirectory"/> (the folder holding the exe), because
        /// the settings file stores them relatively.
        /// </summary>
        public static IReadOnlyList<RegistryEntry> Build(
            string exePath,
            FolderToolsSettings settings,
            string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException("Executable path must not be empty.", nameof(exePath));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var entries = new List<RegistryEntry>();
            IReadOnlyList<Destination> destinations = settings.EnabledDestinations();

            foreach (string target in MenuLayout.FileTargets)
            {
                List<Destination> applicable = destinations
                    .Where(d => AppliesToTarget(d, target))
                    .ToList();

                if (applicable.Count == 0)
                {
                    continue;
                }

                string shell = MenuLayout.ClassesRoot + "\\" + target + @"\shell";

                AddSubmenu(
                    entries,
                    shell + "\\" + CopyRoot,
                    "Копировать в",
                    StoreFor(target, TransferAction.Copy),
                    applicable,
                    TransferAction.Copy,
                    exePath,
                    target,
                    settings,
                    allowCategories: true,
                    baseDirectory);

                AddSubmenu(
                    entries,
                    shell + "\\" + MoveRoot,
                    "Переместить в",
                    StoreFor(target, TransferAction.Move),
                    applicable,
                    TransferAction.Move,
                    exePath,
                    target,
                    settings,
                    allowCategories: true,
                    baseDirectory);
            }

            return entries;
        }

        /// <summary>
        /// True for a shared verb store this layout owns.
        ///
        /// Category submenus get their own store name built from the category index
        /// ("FolderTools.CopyTo.Files.c1"), so a fixed list cannot describe them: the
        /// installer instead scans for this prefix and drops whatever the current
        /// settings no longer use. The main menu's store is excluded because
        /// <see cref="MenuLayout"/> writes it.
        /// </summary>
        public static bool IsOwnedStore(string? subKeyName)
        {
            return !string.IsNullOrEmpty(subKeyName)
                && subKeyName!.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(subKeyName, MenuLayout.VerbStore, StringComparison.OrdinalIgnoreCase);
        }

        private const string StorePrefix = "FolderTools.";

        private const string CopyFiles = "FolderTools.CopyTo.Files";
        private const string MoveFiles = "FolderTools.MoveTo.Files";
        private const string CopyFolders = "FolderTools.CopyTo.Folders";
        private const string MoveFolders = "FolderTools.MoveTo.Folders";

        private static string StoreFor(string target, TransferAction action)
        {
            bool folders = target == "Directory";
            if (action == TransferAction.Copy)
            {
                return folders ? CopyFolders : CopyFiles;
            }

            return folders ? MoveFolders : MoveFiles;
        }

        /// <summary>
        /// True when a destination can act on the given shell target. The "*" target
        /// serves files, so it needs at least one file pattern; the Directory target
        /// needs at least one folder pattern.
        /// </summary>
        public static bool AppliesToTarget(Destination destination, string target)
        {
            if (destination == null || !destination.IsEnabled)
            {
                return false;
            }

            bool hasFolder = destination.Patterns.Any(ExtensionPattern.IsFolderPattern);
            bool hasFile = destination.Patterns
                .Any(p => ExtensionPattern.Normalize(p).Length > 0 && !ExtensionPattern.IsFolderPattern(p));

            if (target == "Directory")
            {
                return hasFolder;
            }

            return hasFile;
        }

        /// <summary>
        /// AQS filter for the union of the given file patterns, or null when every file
        /// matches and no filter is needed. Folder tokens are ignored: they cannot be
        /// expressed as a file-name filter and are handled by the Directory target.
        /// </summary>
        public static string? BuildAppliesTo(IEnumerable<string> patterns)
        {
            var clauses = new List<string>();
            foreach (string pattern in patterns ?? Enumerable.Empty<string>())
            {
                if (ExtensionPattern.IsFolderPattern(pattern))
                {
                    continue;
                }

                string? wildcard = ExtensionPattern.ToShellWildcard(pattern);
                if (wildcard == null)
                {
                    // "*" already matches everything; no filter is more permissive.
                    return null;
                }

                // The filter describes an extension, but the pattern may also constrain
                // the name before it ("photo*.txt"), so anchor it to the end of the name.
                string suffix = wildcard.StartsWith("*", StringComparison.Ordinal)
                    ? wildcard
                    : "*" + wildcard;

                clauses.Add("System.FileName:~" + QuoteAqs(suffix));
            }

            if (clauses.Count == 0)
            {
                return null;
            }

            return clauses.Count == 1 ? clauses[0] : "(" + string.Join(" OR ", clauses) + ")";
        }

        /// <summary>
        /// Registry "Icon" value for a destination: its own icon when one is configured
        /// and still present, otherwise the tool's icon. A missing icon file must not
        /// leave the entry without any icon at all.
        /// </summary>
        public static string? ResolveIconValue(string exePath, string baseDirectory, string? storedIconPath)
        {
            if (!string.IsNullOrWhiteSpace(storedIconPath))
            {
                string resolved = AppPaths.Resolve(baseDirectory, storedIconPath);
                if (File.Exists(resolved))
                {
                    return "\"" + resolved + "\"";
                }
            }

            return null;
        }

        private static string QuoteAqs(string value)
        {
            return "\"" + value.Replace("\"", string.Empty) + "\"";
        }

        /// <summary>
        /// Writes one cascading submenu: its label, the verb-store value, and the verbs
        /// themselves in both the inline and the shared-store location.
        /// </summary>
        private static void AddSubmenu(
            List<RegistryEntry> entries,
            string verbKey,
            string label,
            string storeId,
            IReadOnlyList<Destination> destinations,
            TransferAction action,
            string exePath,
            string target,
            FolderToolsSettings settings,
            bool allowCategories,
            string baseDirectory)
        {
            entries.Add(new RegistryEntry(verbKey, "MUIVerb", label));
            entries.Add(new RegistryEntry(verbKey, "Icon", "\"" + exePath + "\",0"));
            entries.Add(new RegistryEntry(verbKey, "ExtendedSubCommandsKey", storeId));

            // A selection that matches no destination would otherwise open an empty
            // submenu, so the root carries the union filter as well.
            if (target != "Directory")
            {
                string? appliesTo = BuildAppliesTo(destinations.SelectMany(d => d.Patterns));
                if (appliesTo != null)
                {
                    entries.Add(new RegistryEntry(verbKey, "AppliesTo", appliesTo));
                }
            }

            AddChildren(entries, verbKey + @"\ExtendedSubCommandsKey\Shell", storeId, destinations, action, exePath, target, settings, allowCategories, baseDirectory);
            AddChildren(entries, MenuLayout.ClassesRoot + "\\" + storeId + @"\shell", storeId, destinations, action, exePath, target, settings, allowCategories, baseDirectory);
        }

        private static void AddChildren(
            List<RegistryEntry> entries,
            string shellPath,
            string storeId,
            IReadOnlyList<Destination> destinations,
            TransferAction action,
            string exePath,
            string target,
            FolderToolsSettings settings,
            bool allowCategories,
            string baseDirectory)
        {
            List<Destination> topLevel = destinations
                .Where(d => !allowCategories || string.IsNullOrWhiteSpace(d.Category))
                .ToList();

            List<IGrouping<string, Destination>> byCategory = allowCategories
                ? destinations
                    .Where(d => !string.IsNullOrWhiteSpace(d.Category))
                    .GroupBy(d => d.Category!, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<IGrouping<string, Destination>>();

            int index = 1;
            foreach (Destination destination in topLevel)
            {
                AddDestinationVerb(entries, shellPath + "\\" + index.ToString("00"), destination, action, exePath, target, baseDirectory);
                index++;
            }

            int categoryIndex = 1;
            foreach (IGrouping<string, Destination> group in byCategory)
            {
                string categoryVerb = shellPath + @"\cat" + categoryIndex.ToString("00");
                DestinationCategory? category = settings.Categories
                    .FirstOrDefault(c => string.Equals(c.Id, group.Key, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(c.Name, group.Key, StringComparison.OrdinalIgnoreCase));

                AddSubmenu(
                    entries,
                    categoryVerb,
                    category?.Name ?? group.Key,
                    storeId + ".c" + categoryIndex,
                    group.ToList(),
                    action,
                    exePath,
                    target,
                    settings,
                    allowCategories: false,
                    baseDirectory);

                categoryIndex++;
            }
        }

        private static void AddDestinationVerb(
            List<RegistryEntry> entries,
            string verb,
            Destination destination,
            TransferAction action,
            string exePath,
            string target,
            string baseDirectory)
        {
            entries.Add(new RegistryEntry(verb, "MUIVerb", destination.DisplayName));

            string? icon = ResolveIconValue(exePath, baseDirectory, destination.IconPath);
            if (icon != null)
            {
                entries.Add(new RegistryEntry(verb, "Icon", icon));
            }

            if (target != "Directory")
            {
                string? appliesTo = BuildAppliesTo(destination.Patterns);
                if (appliesTo != null)
                {
                    entries.Add(new RegistryEntry(verb, "AppliesTo", appliesTo));
                }
            }

            // Player lets the verb run for any number of selected items; the shell then
            // invokes it once per item, so %1 always names a single file.
            entries.Add(new RegistryEntry(verb, "MultiSelectModel", "Player"));

            string actionName = action == TransferAction.Copy ? "copyto" : "moveto";
            entries.Add(new RegistryEntry(
                verb + @"\command",
                null,
                MenuLayout.BuildFilesCommand(exePath, actionName, destination.Id)));
        }
    }
}
