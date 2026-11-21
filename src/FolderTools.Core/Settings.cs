using System;
using System.Collections.Generic;
using System.Linq;

namespace FolderTools.Core
{
    /// <summary>What a destination entry does with the files routed to it.</summary>
    public enum TransferAction
    {
        Copy,
        Move
    }

    public enum DestinationType
    {
        Local,
        Sftp
    }

    /// <summary>
    /// One destination folder plus the extension patterns that route files to it.
    /// An entry with no patterns never appears in the menu, which is how a folder
    /// can be prepared in the GUI before it is enabled.
    /// </summary>
    public sealed class Destination
    {
        public Destination()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = string.Empty;
            Path = string.Empty;
            Patterns = new List<string>();
        }

        public Destination(string id, string name, string path, IEnumerable<string> patterns)
        {
            Id = id;
            Name = name;
            Path = path;
            Patterns = new List<string>(patterns);
        }

        /// <summary>Stable identity, so renaming a destination does not lose its menu slot.</summary>
        public string Id { get; set; }

        /// <summary>Text shown in the menu; falls back to the folder name when empty.</summary>
        public string Name { get; set; }

        public string Path { get; set; }

        public DestinationType Type { get; set; }

        public string? SftpHost { get; set; }

        public int SftpPort { get; set; } = 22;

        public string? SftpRemotePath { get; set; }

        public string? SftpHostKeyFingerprint { get; set; }

        public string EffectivePath => Type == DestinationType.Sftp
            ? (SftpRemotePath ?? string.Empty)
            : (Path ?? string.Empty);

        /// <summary>Extension patterns such as "*.txt" or "photo!.jpg".</summary>
        public List<string> Patterns { get; set; }

        /// <summary>Category this destination belongs to, or null for the top level.</summary>
        public string? Category { get; set; }

        /// <summary>
        /// Icon for this menu entry, stored relative to the installation folder
        /// ("icons\name.ico") so the whole folder stays movable. Null means the
        /// tool's own icon.
        /// </summary>
        public string? IconPath { get; set; }

        /// <summary>True when at least one usable pattern is configured.</summary>
        public bool IsEnabled => Patterns.Any(p => ExtensionPattern.Normalize(p).Length > 0);

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name.Trim();
                }

                string trimmed = EffectivePath.TrimEnd('\\', '/');
                int slash = trimmed.LastIndexOfAny(new[] { '\\', '/' });
                return slash >= 0 && slash < trimmed.Length - 1 ? trimmed.Substring(slash + 1) : trimmed;
            }
        }

        public bool MatchesExtension(string? extension, bool isDirectory = false)
        {
            return Patterns.Any(p => ExtensionPattern.IsMatch(p, extension, isDirectory));
        }

        /// <summary>
        /// True when the item satisfies any pattern. Uses the full name so a pattern
        /// like "фото!.jpg" routes the same files the shell filter shows the verb for.
        /// </summary>
        public bool MatchesPath(string? path, bool isDirectory = false)
        {
            return Patterns.Any(p => ExtensionPattern.IsMatchName(p, path, isDirectory));
        }

        public Destination Clone()
        {
            return new Destination(Id, Name, Path, Patterns)
            {
                Category = Category,
                IconPath = IconPath,
                Type = Type,
                SftpHost = SftpHost,
                SftpPort = SftpPort,
                SftpRemotePath = SftpRemotePath,
                SftpHostKeyFingerprint = SftpHostKeyFingerprint
            };
        }
    }

    /// <summary>A named group of destinations, shown as one submenu to keep the root short.</summary>
    public sealed class DestinationCategory
    {
        public DestinationCategory()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = string.Empty;
        }

        public DestinationCategory(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public DestinationCategory Clone()
        {
            return new DestinationCategory(Id, Name);
        }
    }

    /// <summary>Everything the user can configure, serialised to JSON next to the exe.</summary>
    public sealed class FolderToolsSettings
    {
        public FolderToolsSettings()
        {
            Categories = new List<DestinationCategory>();
            Destinations = new List<Destination>();
        }

        /// <summary>Schema marker so a future version can migrate an old file.</summary>
        public int SchemaVersion { get; set; } = 1;

        public List<DestinationCategory> Categories { get; set; }

        public List<Destination> Destinations { get; set; }

        /// <summary>
        /// Path to the desktop.ini editor. Stored relatively when the editor sits
        /// inside the installation folder, so the whole folder stays movable.
        /// </summary>
        public string? DeskEditPath { get; set; }

        /// <summary>Whether a confirmation is shown after a successful transfer.</summary>
        public bool ShowSuccess { get; set; }

        public FolderToolsSettings Clone()
        {
            return new FolderToolsSettings
            {
                SchemaVersion = SchemaVersion,
                Categories = Categories.Select(c => c.Clone()).ToList(),
                Destinations = Destinations.Select(d => d.Clone()).ToList(),
                DeskEditPath = DeskEditPath,
                ShowSuccess = ShowSuccess
            };
        }

        /// <summary>Destinations that should appear in the menu for the given action.</summary>
        public IReadOnlyList<Destination> EnabledDestinations()
        {
            return Destinations
                .Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.EffectivePath))
                .ToList();
        }
    }
}
