using System;
using System.Collections.Generic;

namespace FolderTools.Core
{
    public sealed class RemoteProviderDefinition
    {
        public RemoteProviderDefinition(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }
    }

    /// <summary>Parses the provider registry emitted by rclone-compatible engines.</summary>
    public static class RemoteProviderCatalog
    {
        public static IReadOnlyList<RemoteProviderDefinition> Parse(string json)
        {
            JsonObject root = JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("Provider catalogue root must be a JSON object.");
            var providers = new List<RemoteProviderDefinition>();

            JsonArray? wrapped = GetArray(root, "Providers");
            if (wrapped != null)
            {
                foreach (JsonNode item in wrapped.Items)
                {
                    JsonObject? provider = item as JsonObject;
                    if (provider == null) continue;
                    AddProvider(providers, provider, null);
                }
            }
            else
            {
                // rclone and bclone currently marshal fs.Registry directly: the JSON
                // object is keyed by backend name instead of having a Providers array.
                foreach (KeyValuePair<string, JsonNode> entry in root.Members)
                {
                    JsonObject? provider = entry.Value as JsonObject;
                    if (provider == null) continue;
                    AddProvider(providers, provider, entry.Key);
                }
            }

            if (providers.Count == 0)
            {
                throw new JsonException("Provider catalogue contains no recognizable backends.");
            }
            return providers;
        }

        private static JsonArray? GetArray(JsonObject value, string name)
        {
            foreach (KeyValuePair<string, JsonNode> member in value.Members)
            {
                if (string.Equals(member.Key, name, StringComparison.OrdinalIgnoreCase))
                    return member.Value as JsonArray;
            }
            return null;
        }

        private static string? GetString(JsonObject value, string name)
        {
            foreach (KeyValuePair<string, JsonNode> member in value.Members)
            {
                if (string.Equals(member.Key, name, StringComparison.OrdinalIgnoreCase))
                    return (member.Value as JsonString)?.Value;
            }
            return null;
        }

        private static void AddProvider(List<RemoteProviderDefinition> providers, JsonObject provider, string? fallbackName)
        {
            string name = GetString(provider, "Name") ?? fallbackName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return;
            string description = GetString(provider, "Description") ?? string.Empty;
            providers.Add(new RemoteProviderDefinition(name, description));
        }
    }
}
