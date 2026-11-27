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
            var providers = new List<RemoteProviderDefinition>();
            JsonNode root = JsonNode.Parse(json);
            if (root is JsonArray directArray)
            {
                AddProviders(providers, directArray);
            }
            else if (root is JsonObject rootObject)
            {
                JsonArray? wrapped = GetArray(rootObject, "Providers");
                if (wrapped != null)
                {
                    AddProviders(providers, wrapped);
                }
                else
                {
                    // Retain support for wrappers that key provider objects by name.
                    foreach (KeyValuePair<string, JsonNode> entry in rootObject.Members)
                    {
                        JsonObject? provider = entry.Value as JsonObject;
                        if (provider == null) continue;
                        AddProvider(providers, provider, entry.Key);
                    }
                }
            }
            else
            {
                throw new JsonException("Provider catalogue root must be a JSON array or object.");
            }

            if (providers.Count == 0)
            {
                throw new JsonException("Provider catalogue contains no recognizable backends.");
            }
            return providers;
        }

        private static void AddProviders(List<RemoteProviderDefinition> providers, JsonArray array)
        {
            foreach (JsonNode item in array.Items)
            {
                JsonObject? provider = item as JsonObject;
                if (provider != null) AddProvider(providers, provider, null);
            }
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
