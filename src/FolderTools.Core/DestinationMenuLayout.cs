using System;
using System.Collections.Generic;
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
                AddAction(entries, shell, CopyRoot, "Копировать в", TransferAction.Copy, exePath, applicable, settings, target);
                AddAction(entries, shell, MoveRoot, "Переместить в", TransferAction.Move, exePath, applicable, settings, target);
            }

            return entries;
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

        private static string QuoteAqs(string value)
        {
            return "\"" + value.Replace("\"", string.Empty) + "\"";
        }

        private static void AddAction(
            List<RegistryEntry> entries,
            string shell,
            string rootVerb,
            string rootLabel,
            TransferAction action,
            string exePath,
            IReadOnlyList<Destination> destinations,
            FolderToolsSettings settings,
            string target)
        {
            string root = shell + "\\" + rootVerb;
            entries.Add(new RegistryEntry(root, "MUIVerb", rootLabel));
            entries.Add(new RegistryEntry(root, "Icon", "\"" + exePath + "\",0"));

            // Without a filter on the root, a selection that matches no destination
            // would still show an empty submenu.
            string? rootAppliesTo = target == "Directory"
                ? null
                : BuildAppliesTo(destinations.SelectMany(d => d.Patterns));
            if (rootAppliesTo != null)
            {
                entries.Add(new RegistryEntry(root, "AppliesTo", rootAppliesTo));
            }

            entries.Add(new RegistryEntry(root, "SubCommands", string.Empty));

            List<Destination> topLevel = destinations
                .Where(d => string.IsNullOrWhiteSpace(d.Category))
                .ToList();

            List<IGrouping<string, Destination>> byCategory = destinations
                .Where(d => !string.IsNullOrWhiteSpace(d.Category))
                .GroupBy(d => d.Category!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int index = 1;
            foreach (Destination destination in topLevel)
            {
                AddDestinationVerb(entries, root + @"\shell\" + index.ToString("00"), destination, action, exePath, target);
                index++;
            }

            int categoryIndex = 1;
            foreach (IGrouping<string, Destination> group in byCategory)
            {
                string categoryVerb = root + @"\shell\cat" + categoryIndex.ToString("00");
                DestinationCategory? category = settings.Categories
                    .FirstOrDefault(c => string.Equals(c.Id, group.Key, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(c.Name, group.Key, StringComparison.OrdinalIgnoreCase));

                entries.Add(new RegistryEntry(categoryVerb, "MUIVerb", category?.Name ?? group.Key));
                entries.Add(new RegistryEntry(categoryVerb, "SubCommands", string.Empty));

                int childIndex = 1;
                foreach (Destination destination in group)
                {
                    AddDestinationVerb(entries, categoryVerb + @"\shell\" + childIndex.ToString("00"), destination, action, exePath, target);
                    childIndex++;
                }

                categoryIndex++;
            }
        }

        private static void AddDestinationVerb(
            List<RegistryEntry> entries,
            string verb,
            Destination destination,
            TransferAction action,
            string exePath,
            string target)
        {
            entries.Add(new RegistryEntry(verb, "MUIVerb", destination.DisplayName));

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
