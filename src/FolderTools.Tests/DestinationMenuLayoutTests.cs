using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class DestinationMenuLayoutTests
    {
        private const string Exe = @"C:\Tools\FolderTools\FolderTools.exe";

        private static FolderToolsSettings SettingsWith(params Destination[] destinations)
        {
            var settings = new FolderToolsSettings();
            foreach (Destination destination in destinations)
            {
                settings.Destinations.Add(destination);
            }

            return settings;
        }

        [Fact]
        public void Build_CreatesCopyAndMoveRootsForFilesAndFolders()
        {
            var settings = SettingsWith(
                new Destination("d1", "Text", @"D:\Text", new[] { "*.txt" }),
                new Destination("d2", "Dirs", @"D:\Dirs", new[] { ExtensionPattern.FolderToken }));

            var entries = DestinationMenuLayout.Build(Exe, settings);

            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\*\shell\01copyto" && e.Name == "MUIVerb" && e.Value == "Копировать в");
            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\*\shell\02moveto" && e.Name == "MUIVerb" && e.Value == "Переместить в");
            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\Directory\shell\01copyto");
            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\Directory\shell\02moveto");
        }

        [Fact]
        public void Build_FileDestinationIsNotRegisteredForFolders()
        {
            var settings = SettingsWith(new Destination("d1", "Text", @"D:\Text", new[] { "*.txt" }));

            var entries = DestinationMenuLayout.Build(Exe, settings);

            Assert.Contains(entries, e => e.KeyPath.StartsWith(@"Software\Classes\*\shell\01copyto"));
            Assert.DoesNotContain(entries, e => e.KeyPath.StartsWith(@"Software\Classes\Directory\shell\01copyto"));
        }

        [Fact]
        public void Build_FolderDestinationIsNotRegisteredForFiles()
        {
            var settings = SettingsWith(new Destination("d1", "Dirs", @"D:\Dirs", new[] { ExtensionPattern.FolderToken }));

            var entries = DestinationMenuLayout.Build(Exe, settings);

            Assert.Contains(entries, e => e.KeyPath.StartsWith(@"Software\Classes\Directory\shell\01copyto"));
            Assert.DoesNotContain(entries, e => e.KeyPath.StartsWith(@"Software\Classes\*\shell\01copyto"));
        }

        [Fact]
        public void Build_SkipsDestinationWithoutPatterns()
        {
            var settings = SettingsWith(new Destination("d1", "Empty", @"D:\Empty", new string[0]));

            Assert.Empty(DestinationMenuLayout.Build(Exe, settings));
        }

        [Fact]
        public void Build_VerbCarriesCommandWithDestinationId()
        {
            var settings = SettingsWith(new Destination("abc123", "Text", @"D:\Text", new[] { "*.txt" }));

            var entries = DestinationMenuLayout.Build(Exe, settings);
            var command = entries.Single(e => e.KeyPath == @"Software\Classes\*\shell\01copyto\shell\01\command");

            Assert.Equal("\"" + Exe + "\" copyto \"abc123\" \"%1\"", command.Value);
        }

        [Fact]
        public void Build_MoveUsesMovetoVerb()
        {
            var settings = SettingsWith(new Destination("abc123", "Text", @"D:\Text", new[] { "*.txt" }));

            var entries = DestinationMenuLayout.Build(Exe, settings);
            var command = entries.Single(e => e.KeyPath == @"Software\Classes\*\shell\02moveto\shell\01\command");

            Assert.Contains(" moveto ", command.Value);
        }

        [Fact]
        public void Build_GroupsCategorizedDestinationsUnderOneSubmenu()
        {
            var settings = new FolderToolsSettings();
            settings.Categories.Add(new DestinationCategory("cat1", "Фото"));
            settings.Destinations.Add(new Destination("d1", "Снимки", @"D:\Photos", new[] { "*.jpg" }) { Category = "cat1" });
            settings.Destinations.Add(new Destination("d2", "Скрины", @"D:\Shots", new[] { "*.png" }) { Category = "cat1" });
            settings.Destinations.Add(new Destination("d3", "Текст", @"D:\Text", new[] { "*.txt" }));

            var entries = DestinationMenuLayout.Build(Exe, settings);

            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\*\shell\01copyto\shell\cat01" && e.Name == "MUIVerb" && e.Value == "Фото");
            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\*\shell\01copyto\shell\cat01\shell\01\command");
            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\*\shell\01copyto\shell\cat01\shell\02\command");
            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\*\shell\01copyto\shell\01\command");
        }

        [Fact]
        public void Build_UncategorizedDestinationStaysAtTopLevel()
        {
            var settings = new FolderToolsSettings();
            settings.Categories.Add(new DestinationCategory("cat1", "Фото"));
            settings.Destinations.Add(new Destination("d1", "Снимки", @"D:\Photos", new[] { "*.jpg" }) { Category = "cat1" });
            settings.Destinations.Add(new Destination("d2", "Текст", @"D:\Text", new[] { "*.txt" }));

            var entries = DestinationMenuLayout.Build(Exe, settings);
            var topLevel = entries.Single(e => e.KeyPath == @"Software\Classes\*\shell\01copyto\shell\01" && e.Name == "MUIVerb");

            Assert.Equal("Текст", topLevel.Value);
        }

        [Fact]
        public void BuildAppliesTo_UsesSingleClauseForOnePattern()
        {
            Assert.Equal(
                "System.FileName:~\"*.txt\"",
                DestinationMenuLayout.BuildAppliesTo(new[] { "*.txt" }));
        }

        [Fact]
        public void BuildAppliesTo_JoinsMultiplePatternsWithOr()
        {
            string? result = DestinationMenuLayout.BuildAppliesTo(new[] { "*.txt", "*.md" });

            Assert.Equal("(System.FileName:~\"*.txt\" OR System.FileName:~\"*.md\")", result);
        }

        [Fact]
        public void BuildAppliesTo_ReturnsNullWhenAnyPatternMatchesEverything()
        {
            Assert.Null(DestinationMenuLayout.BuildAppliesTo(new[] { "*.txt", "*" }));
        }

        [Fact]
        public void BuildAppliesTo_TranslatesExclamationToOneCharacterWildcard()
        {
            Assert.Equal(
                "System.FileName:~\"*.jp?\"",
                DestinationMenuLayout.BuildAppliesTo(new[] { "*.jp!" }));
        }

        [Fact]
        public void BuildAppliesTo_AnchorsNameConstrainedPatternToTheEnd()
        {
            Assert.Equal(
                "System.FileName:~\"*photo*.txt\"",
                DestinationMenuLayout.BuildAppliesTo(new[] { "photo*.txt" }));
        }

        [Fact]
        public void BuildAppliesTo_IgnoresFolderToken()
        {
            Assert.Null(DestinationMenuLayout.BuildAppliesTo(new[] { ExtensionPattern.FolderToken }));
        }

        [Fact]
        public void Build_RootCarriesFilterSoEmptySubmenuIsHidden()
        {
            var settings = SettingsWith(new Destination("d1", "Text", @"D:\Text", new[] { "*.txt" }));

            var entries = DestinationMenuLayout.Build(Exe, settings);
            var root = entries.Single(e => e.KeyPath == @"Software\Classes\*\shell\01copyto" && e.Name == "AppliesTo");

            Assert.Equal("System.FileName:~\"*.txt\"", root.Value);
        }

        [Fact]
        public void Build_RootFilterUnionsEveryDestination()
        {
            var settings = SettingsWith(
                new Destination("d1", "Text", @"D:\Text", new[] { "*.txt" }),
                new Destination("d2", "Images", @"D:\Images", new[] { "*.jpg" }));

            var entries = DestinationMenuLayout.Build(Exe, settings);
            var root = entries.Single(e => e.KeyPath == @"Software\Classes\*\shell\01copyto" && e.Name == "AppliesTo");

            Assert.Equal("(System.FileName:~\"*.txt\" OR System.FileName:~\"*.jpg\")", root.Value);
        }

        [Fact]
        public void Build_WithoutExecutable_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => DestinationMenuLayout.Build("  ", new FolderToolsSettings()));
        }
    }
}
