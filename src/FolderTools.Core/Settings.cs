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

    /// <summary>An additional SFTP address for the same destination and credentials.</summary>
    public sealed class SftpEndpoint
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string? HostKeyFingerprint { get; set; }

        /// <summary>Runtime marker; primary endpoint is persisted in Destination's legacy fields.</summary>
        public bool IsPrimary { get; set; }

        public SftpEndpoint Clone()
        {
            return new SftpEndpoint { Host = Host, Port = Port, HostKeyFingerprint = HostKeyFingerprint, IsPrimary = IsPrimary };
        }
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
            SftpFallbacks = new List<SftpEndpoint>();
            SftpChmodExtensions = new List<string>();
            SftpChmodMode = "0644";
        }

        public Destination(string id, string name, string path, IEnumerable<string> patterns)
        {
            Id = id;
            Name = name;
            Path = path;
            Patterns = new List<string>(patterns);
            SftpFallbacks = new List<SftpEndpoint>();
            SftpChmodExtensions = new List<string>();
            SftpChmodMode = "0644";
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

        /// <summary>Alternative host/port pairs, each with its own pinned SSH key.</summary>
        public List<SftpEndpoint> SftpFallbacks { get; set; }

        /// <summary>Extensions to chmod after successful publication; values include the leading dot.</summary>
        public List<string> SftpChmodExtensions { get; set; }

        /// <summary>Unix permission mode in octal notation, e.g. 0644 or 0755.</summary>
        public string SftpChmodMode { get; set; }

        /// <summary>Username may travel with the portable config; the password never does in clear text.</summary>
        public string? SftpUsername { get; set; }

        /// <summary>Authenticated, master-password-encrypted portable SFTP password payload.</summary>
        public string? SftpPasswordEncrypted { get; set; }

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
                SftpHostKeyFingerprint = SftpHostKeyFingerprint,
                SftpFallbacks = (SftpFallbacks ?? new List<SftpEndpoint>()).Select(e => e.Clone()).ToList(),
                SftpChmodExtensions = new List<string>(SftpChmodExtensions ?? new List<string>()),
                SftpChmodMode = SftpChmodMode,
                SftpUsername = SftpUsername,
                SftpPasswordEncrypted = SftpPasswordEncrypted
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
            RemoteEngine = "rclone";
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

        /// <summary>Selected optional cloud backend engine; values are "rclone" or "bclone".</summary>
        public string RemoteEngine { get; set; }

        /// <summary>Optional reminder for the master password. It is not secret and is not used as key material.</summary>
        public string? MasterPasswordHint { get; set; }

        public FolderToolsSettings Clone()
        {
            return new FolderToolsSettings
            {
                SchemaVersion = SchemaVersion,
                Categories = Categories.Select(c => c.Clone()).ToList(),
                Destinations = Destinations.Select(d => d.Clone()).ToList(),
                DeskEditPath = DeskEditPath,
                ShowSuccess = ShowSuccess,
                MasterPasswordHint = MasterPasswordHint,
                RemoteEngine = RemoteEngine
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
