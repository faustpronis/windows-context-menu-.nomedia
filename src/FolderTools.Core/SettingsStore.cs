using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FolderTools.Core
{
    /// <summary>
    /// Reads and writes <see cref="FolderToolsSettings"/> as JSON.
    ///
    /// The file sits next to the executable, which keeps settings portable with the
    /// program and avoids the registry for anything larger than a single value. The
    /// exe path is registered in HKCU, so moving the folder breaks the menu anyway;
    /// keeping settings beside the exe means one move, not two.
    /// </summary>
    public static class SettingsStore
    {
        public const string FileName = "FolderTools.settings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string DefaultPath(string baseDirectory)
        {
            return Path.Combine(baseDirectory, FileName);
        }

        /// <summary>Loads settings, returning empty settings when the file is absent.</summary>
        public static FolderToolsSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new FolderToolsSettings();
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new FolderToolsSettings();
            }

            try
            {
                return FromJson(JsonNode.Parse(text) as JsonObject);
            }
            catch (JsonException)
            {
                // A hand-edited or truncated file must not make the whole tool unusable:
                // the menu then falls back to its built-in entries.
                return new FolderToolsSettings();
            }
        }

        /// <summary>
        /// Writes settings, creating the directory if needed.
        ///
        /// The new content is written to a temporary file and then swapped in with
        /// File.Replace, so an interrupted save leaves either the old file or the new
        /// one, never a truncated file and never no file at all.
        /// </summary>
        public static void Save(string path, FolderToolsSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = path + ".tmp";
            File.WriteAllText(temp, ToJson(settings).ToJson(), Utf8NoBom);

            if (File.Exists(path))
            {
                string backup = path + ".bak";
                File.Replace(temp, path, backup, true);
                TryDelete(backup);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }

        public static JsonObject ToJson(FolderToolsSettings settings)
        {
            var categories = new JsonArray();
            foreach (DestinationCategory category in settings.Categories)
            {
                categories.Add(new JsonObject()
                    .Add("id", category.Id)
                    .Add("name", category.Name));
            }

            var destinations = new JsonArray();
            foreach (Destination destination in settings.Destinations)
            {
                var entry = new JsonObject()
                    .Add("id", destination.Id)
                    .Add("name", destination.Name)
                    .Add("path", destination.Path)
                    .Add("patterns", destination.Patterns);

                if (!string.IsNullOrWhiteSpace(destination.Category))
                {
                    entry.Add("category", destination.Category);
                }

                if (!string.IsNullOrWhiteSpace(destination.IconPath))
                {
                    entry.Add("icon", destination.IconPath);
                }

                if (destination.Type == DestinationType.Sftp)
                {
                    entry.Add("type", "sftp")
                        .Add("sftpHost", destination.SftpHost ?? string.Empty)
                        .Add("sftpPort", destination.SftpPort)
                        .Add("sftpRemotePath", destination.SftpRemotePath ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(destination.SftpHostKeyFingerprint))
                    {
                        entry.Add("sftpHostKeyFingerprint", destination.SftpHostKeyFingerprint);
                    }
                }

                destinations.Add(entry);
            }

            var root = new JsonObject()
                .Add("schemaVersion", settings.SchemaVersion)
                .Add("categories", categories)
                .Add("destinations", destinations);

            if (!string.IsNullOrWhiteSpace(settings.DeskEditPath))
            {
                root.Add("deskEditPath", settings.DeskEditPath);
            }

            if (settings.ShowSuccess)
            {
                root.Add("showSuccess", true);
            }

            return root;
        }

        /// <summary>
        /// Rebuilds settings from JSON, tolerating missing fields. Unknown members are
        /// ignored so a newer file can still be read by an older build.
        /// </summary>
        public static FolderToolsSettings FromJson(JsonObject? root)
        {
            var settings = new FolderToolsSettings();
            if (root == null)
            {
                return settings;
            }

            settings.SchemaVersion = root.GetInt("schemaVersion", 1);
            settings.DeskEditPath = root.GetString("deskEditPath");
            settings.ShowSuccess = root.GetBool("showSuccess", false);

            JsonArray? categories = root.GetArray("categories");
            if (categories != null)
            {
                foreach (JsonNode item in categories.Items)
                {
                    if (item is JsonObject category)
                    {
                        settings.Categories.Add(new DestinationCategory(
                            category.GetString("id") ?? Guid.NewGuid().ToString("N"),
                            category.GetString("name") ?? string.Empty));
                    }
                }
            }

            JsonArray? destinations = root.GetArray("destinations");
            if (destinations != null)
            {
                foreach (JsonNode item in destinations.Items)
                {
                    if (!(item is JsonObject entry))
                    {
                        continue;
                    }

                    var patterns = new List<string>();
                    JsonArray? patternArray = entry.GetArray("patterns");
                    if (patternArray != null)
                    {
                        foreach (JsonNode pattern in patternArray.Items)
                        {
                            if (pattern is JsonString text && !string.IsNullOrWhiteSpace(text.Value))
                            {
                                patterns.Add(text.Value);
                            }
                        }
                    }

                    settings.Destinations.Add(new Destination(
                        entry.GetString("id") ?? Guid.NewGuid().ToString("N"),
                        entry.GetString("name") ?? string.Empty,
                        entry.GetString("path") ?? string.Empty,
                        patterns)
                    {
                        Category = entry.GetString("category"),
                        IconPath = entry.GetString("icon"),
                        Type = string.Equals(entry.GetString("type"), "sftp", StringComparison.OrdinalIgnoreCase)
                            ? DestinationType.Sftp
                            : DestinationType.Local,
                        SftpHost = entry.GetString("sftpHost"),
                        SftpPort = entry.GetInt("sftpPort", 22),
                        SftpRemotePath = entry.GetString("sftpRemotePath"),
                        SftpHostKeyFingerprint = entry.GetString("sftpHostKeyFingerprint")
                    });
                }
            }

            return settings;
        }
    }
}
