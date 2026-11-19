using System.Collections.Generic;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class DeskeditServiceTests
    {
        private sealed class RecordingLauncher : IProcessLauncher
        {
            public string? FileName;
            public string? Arguments;

            public void Launch(string fileName, string arguments)
            {
                FileName = fileName;
                Arguments = arguments;
            }
        }

        [Fact]
        public void OpenFolder_LaunchesEditorWithQuotedFolder()
        {
            var launcher = new RecordingLauncher();
            var service = new DeskeditService(launcher, _ => true);

            DeskeditOutcome outcome = service.OpenFolder(@"C:\Tools\DeskEdit\DeskEdit_x64.exe", @"D:\My Folder");

            Assert.Equal(DeskeditOutcome.Launched, outcome);
            Assert.Equal(@"C:\Tools\DeskEdit\DeskEdit_x64.exe", launcher.FileName);
            Assert.Equal("\"D:\\My Folder\"", launcher.Arguments);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void OpenFolder_WithoutEditor_ReportsMissing(string? editor)
        {
            var service = new DeskeditService(new RecordingLauncher(), _ => true);
            Assert.Equal(DeskeditOutcome.EditorMissing, service.OpenFolder(editor, @"D:\X"));
        }

        [Fact]
        public void OpenFolder_WhenEditorFileAbsent_ReportsMissing()
        {
            var service = new DeskeditService(new RecordingLauncher(), _ => false);
            Assert.Equal(DeskeditOutcome.EditorMissing, service.OpenFolder(@"C:\missing.exe", @"D:\X"));
        }
    }

    public class CommandLineTests
    {
        [Fact]
        public void Parse_NoArguments_ShowsHelp()
        {
            Assert.Equal(CommandKind.Help, CommandLine.Parse(new List<string>()).Kind);
        }

        [Fact]
        public void Parse_NomediaWithFolder()
        {
            ParsedCommand command = CommandLine.Parse(new List<string> { "nomedia", @"D:\Share" });
            Assert.Equal(CommandKind.Nomedia, command.Kind);
            Assert.Equal(@"D:\Share", command.Folder);
        }

        [Fact]
        public void Parse_VerbIsCaseInsensitive()
        {
            Assert.Equal(CommandKind.Deskedit, CommandLine.Parse(new List<string> { "DeskEdit", @"D:\X" }).Kind);
        }

        [Fact]
        public void Parse_SetDeskEdit_KeepsPathAsValue()
        {
            ParsedCommand command = CommandLine.Parse(new List<string> { "set-deskedit", @"C:\Tools\DeskEdit.exe" });
            Assert.Equal(CommandKind.SetDeskEdit, command.Kind);
            Assert.Equal(@"C:\Tools\DeskEdit.exe", command.Value);
        }

        [Fact]
        public void Parse_BarePath_IsTreatedAsNomediaTarget()
        {
            ParsedCommand command = CommandLine.Parse(new List<string> { @"D:\Share\Sync" });
            Assert.Equal(CommandKind.Nomedia, command.Kind);
            Assert.Equal(@"D:\Share\Sync", command.Folder);
        }

        [Fact]
        public void Parse_UnknownVerb_IsUnknown()
        {
            Assert.Equal(CommandKind.Unknown, CommandLine.Parse(new List<string> { "bogus" }).Kind);
        }

        [Theory]
        [InlineData("install")]
        [InlineData("-i")]
        [InlineData("--install")]
        [InlineData("/i")]
        [InlineData("-I")]
        [InlineData("--INSTALL")]
        public void Parse_InstallAliases(string verb)
        {
            Assert.Equal(CommandKind.Install, CommandLine.Parse(new List<string> { verb }).Kind);
        }

        [Theory]
        [InlineData("uninstall")]
        [InlineData("remove")]
        [InlineData("-r")]
        [InlineData("-remove")]
        [InlineData("--remove")]
        [InlineData("--uninstall")]
        [InlineData("/r")]
        [InlineData("-R")]
        public void Parse_UninstallAliases(string verb)
        {
            Assert.Equal(CommandKind.Uninstall, CommandLine.Parse(new List<string> { verb }).Kind);
        }

        [Theory]
        [InlineData("-v")]
        [InlineData("--version")]
        [InlineData("version")]
        [InlineData("/v")]
        public void Parse_VersionAliases(string verb)
        {
            Assert.Equal(CommandKind.Version, CommandLine.Parse(new List<string> { verb }).Kind);
        }

        [Theory]
        [InlineData("-?")]
        [InlineData("-h")]
        [InlineData("--help")]
        public void Parse_HelpAliases(string verb)
        {
            Assert.Equal(CommandKind.Help, CommandLine.Parse(new List<string> { verb }).Kind);
        }

        [Fact]
        public void HelpText_MentionsInstallAndRemove()
        {
            Assert.Contains("-i", CommandLine.HelpText);
            Assert.Contains("--install", CommandLine.HelpText);
            Assert.Contains("-r", CommandLine.HelpText);
        }
    }
}
