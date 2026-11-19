using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class TransferCommandLineTests
    {
        [Fact]
        public void CopyTo_ParsesDestinationAndSingleItem()
        {
            ParsedCommand command = CommandLine.Parse(new[] { "copyto", "abc123", @"C:\a.txt" });

            Assert.Equal(CommandKind.CopyTo, command.Kind);
            Assert.Equal("abc123", command.Folder);
            Assert.Equal(new[] { @"C:\a.txt" }, command.Items);
        }

        [Fact]
        public void MoveTo_ParsesDestinationAndSingleItem()
        {
            ParsedCommand command = CommandLine.Parse(new[] { "moveto", "abc123", @"C:\a.txt" });

            Assert.Equal(CommandKind.MoveTo, command.Kind);
            Assert.Equal("abc123", command.Folder);
        }

        [Fact]
        public void CopyTo_AcceptsSeveralItemsForManualUse()
        {
            ParsedCommand command = CommandLine.Parse(new[] { "copyto", "id", @"C:\a.txt", @"C:\b.jpg" });

            Assert.Equal(2, command.Items.Count);
        }

        [Fact]
        public void CopyTo_WithoutItems_YieldsEmptyList()
        {
            ParsedCommand command = CommandLine.Parse(new[] { "copyto", "id" });

            Assert.Empty(command.Items);
        }

        [Theory]
        [InlineData("COPYTO")]
        [InlineData("CopyTo")]
        public void CopyTo_IsCaseInsensitive(string verb)
        {
            Assert.Equal(CommandKind.CopyTo, CommandLine.Parse(new[] { verb, "id", "x" }).Kind);
        }

        [Theory]
        [InlineData("settings")]
        [InlineData("SETTINGS")]
        [InlineData("config")]
        public void Settings_IsRecognised(string verb)
        {
            Assert.Equal(CommandKind.Settings, CommandLine.Parse(new[] { verb }).Kind);
        }

        [Fact]
        public void Help_MentionsSettings()
        {
            Assert.Contains("settings", CommandLine.HelpText);
        }

        [Fact]
        public void ExistingVerbsStillParse()
        {
            Assert.Equal(CommandKind.Nomedia, CommandLine.Parse(new[] { "nomedia", @"C:\x" }).Kind);
            Assert.Equal(CommandKind.Deskedit, CommandLine.Parse(new[] { "deskedit", @"C:\x" }).Kind);
            Assert.Equal(CommandKind.Install, CommandLine.Parse(new[] { "-i" }).Kind);
            Assert.Equal(CommandKind.Uninstall, CommandLine.Parse(new[] { "-r" }).Kind);
            Assert.Equal(CommandKind.Version, CommandLine.Parse(new[] { "-v" }).Kind);
        }
    }
}
