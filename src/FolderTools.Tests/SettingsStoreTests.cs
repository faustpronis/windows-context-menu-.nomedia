using System.Collections.Generic;
using System.IO;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class SettingsStoreTests
    {
        [Fact]
        public void RoundTrip_KeepsDestinationsAndCategories()
        {
            var settings = new FolderToolsSettings();
            var category = new DestinationCategory("cat1", "Фото");
            settings.Categories.Add(category);
            settings.Destinations.Add(new Destination("d1", "Снимки", @"D:\Photos", new[] { "*.jpg", "*.jp!" })
            {
                Category = "cat1"
            });
            settings.Destinations.Add(new Destination("d2", "Прочее", @"D:\Other", new[] { "*" }));

            string json = SettingsStore.ToJson(settings).ToJson();
            FolderToolsSettings restored = SettingsStore.FromJson(JsonNode.Parse(json) as JsonObject);

            Assert.Equal(2, restored.Destinations.Count);
            Assert.Equal("Снимки", restored.Destinations[0].Name);
            Assert.Equal(@"D:\Photos", restored.Destinations[0].Path);
            Assert.Equal(new[] { "*.jpg", "*.jp!" }, restored.Destinations[0].Patterns);
            Assert.Equal("cat1", restored.Destinations[0].Category);
            Assert.Null(restored.Destinations[1].Category);
            Assert.Single(restored.Categories);
            Assert.Equal("Фото", restored.Categories[0].Name);
        }

        [Fact]
        public void Json_EscapesWindowsPaths()
        {
            var settings = new FolderToolsSettings();
            settings.Destinations.Add(new Destination("d1", "A", @"C:\Temp\a", new[] { "*.txt" }));

            string json = SettingsStore.ToJson(settings).ToJson();

            Assert.Contains(@"C:\\Temp\\a", json);
            Assert.DoesNotContain("\t", json);
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmptySettings()
        {
            FolderToolsSettings settings = SettingsStore.Load(Path.Combine(Path.GetTempPath(), "no-such-file.json"));

            Assert.Empty(settings.Destinations);
            Assert.Empty(settings.Categories);
        }

        [Fact]
        public void FromJson_ToleratesMissingFields()
        {
            var root = JsonNode.Parse("{\"destinations\":[{\"path\":\"D:\\\\X\"}]}") as JsonObject;

            FolderToolsSettings settings = SettingsStore.FromJson(root);

            Assert.Single(settings.Destinations);
            Assert.Equal(@"D:\X", settings.Destinations[0].Path);
            Assert.Empty(settings.Destinations[0].Patterns);
            Assert.NotNull(settings.Destinations[0].Id);
        }

        [Fact]
        public void FromJson_Null_ReturnsEmptySettings()
        {
            Assert.Empty(SettingsStore.FromJson(null).Destinations);
        }

        [Fact]
        public void Save_And_Load_OnDisk()
        {
            string path = Path.Combine(Path.GetTempPath(), "foldertools-test-settings.json");
            try
            {
                var settings = new FolderToolsSettings();
                settings.Destinations.Add(new Destination("d1", "A", @"D:\A", new[] { "*.txt" }));

                SettingsStore.Save(path, settings);
                FolderToolsSettings loaded = SettingsStore.Load(path);

                Assert.Single(loaded.Destinations);
                Assert.Equal(@"D:\A", loaded.Destinations[0].Path);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void Load_CorruptedFile_ReturnsEmptySettingsInsteadOfThrowing()
        {
            string path = Path.Combine(Path.GetTempPath(), "foldertools-broken.json");
            try
            {
                File.WriteAllText(path, "{ \"destinations\": [ {\"path\": }");

                FolderToolsSettings settings = SettingsStore.Load(path);

                Assert.Empty(settings.Destinations);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void Save_OverwritesExistingFileWithoutBackupLeftBehind()
        {
            string path = Path.Combine(Path.GetTempPath(), "foldertools-overwrite.json");
            try
            {
                var first = new FolderToolsSettings();
                first.Destinations.Add(new Destination("d1", "A", @"D:\A", new[] { "*.txt" }));
                SettingsStore.Save(path, first);

                var second = new FolderToolsSettings();
                second.Destinations.Add(new Destination("d2", "B", @"D:\B", new[] { "*.jpg" }));
                SettingsStore.Save(path, second);

                FolderToolsSettings loaded = SettingsStore.Load(path);
                Assert.Single(loaded.Destinations);
                Assert.Equal(@"D:\B", loaded.Destinations[0].Path);
                Assert.False(File.Exists(path + ".bak"));
                Assert.False(File.Exists(path + ".tmp"));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void Save_DoesNotLeaveTemporaryFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "foldertools-tmp-check.json");
            try
            {
                SettingsStore.Save(path, new FolderToolsSettings());
                Assert.False(File.Exists(path + ".tmp"));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    public class DestinationTests
    {
        [Fact]
        public void DisplayName_FallsBackToFolderName()
        {
            var destination = new Destination("d", string.Empty, @"D:\Share\Photos\", new List<string>());

            Assert.Equal("Photos", destination.DisplayName);
        }

        [Fact]
        public void DisplayName_PrefersConfiguredName()
        {
            var destination = new Destination("d", "  Мои фото  ", @"D:\Share\Photos", new List<string>());

            Assert.Equal("Мои фото", destination.DisplayName);
        }

        [Fact]
        public void IsEnabled_RequiresAtLeastOnePattern()
        {
            Assert.False(new Destination("d", "A", @"D:\A", new string[0]).IsEnabled);
            Assert.False(new Destination("d", "A", @"D:\A", new[] { "  " }).IsEnabled);
            Assert.True(new Destination("d", "A", @"D:\A", new[] { "*.txt" }).IsEnabled);
        }

        [Fact]
        public void EnabledDestinations_SkipsDisabledAndEmptyPaths()
        {
            var settings = new FolderToolsSettings();
            settings.Destinations.Add(new Destination("d1", "A", @"D:\A", new[] { "*.txt" }));
            settings.Destinations.Add(new Destination("d2", "B", @"D:\B", new string[0]));
            settings.Destinations.Add(new Destination("d3", "C", "  ", new[] { "*.txt" }));

            Assert.Single(settings.EnabledDestinations());
        }
    }
}
