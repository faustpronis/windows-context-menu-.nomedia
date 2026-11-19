using System.Collections.Generic;
using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class MenuLayoutTests
    {
        private const string Exe = @"C:\Tools\FolderTools\FolderTools.exe";

        [Theory]
        [InlineData(@"Software\Classes\Directory\Background\shell\FolderTools")]
        [InlineData(@"Software\Classes\Directory\shell\FolderTools")]
        [InlineData(@"Software\Classes\Drive\shell\FolderTools")]
        public void Build_RegistersMenuForEveryTarget(string parent)
        {
            var entries = MenuLayout.Build(Exe);

            Assert.Contains(entries, e => e.KeyPath == parent && e.Name == "MUIVerb" && e.Value == "FolderTools");
            Assert.Contains(entries, e => e.KeyPath == parent && e.Name == "Icon" && e.Value.Contains("FolderTools.exe"));
            Assert.Contains(entries, e => e.KeyPath == parent && e.Name == "ExtendedSubCommandsKey" && e.Value == MenuLayout.VerbStore);
        }

        [Theory]
        [InlineData(@"Software\Classes\Directory\Background\shell\FolderTools")]
        [InlineData(@"Software\Classes\Directory\shell\FolderTools")]
        [InlineData(@"Software\Classes\Drive\shell\FolderTools")]
        public void Build_UsesDocumentedExtendedSubCommandsKeySubkey(string parent)
        {
            var entries = MenuLayout.Build(Exe);
            string verbs = parent + @"\ExtendedSubCommandsKey\Shell";

            Assert.Contains(entries, e => e.KeyPath == verbs + @"\01nomedia\command");
            Assert.Contains(entries, e => e.KeyPath == verbs + @"\02deskedit\command");
            Assert.Contains(entries, e => e.KeyPath == verbs + @"\01nomedia" && e.Name == "MUIVerb" && e.Value == "Создать .nomedia");
            Assert.Contains(entries, e => e.KeyPath == verbs + @"\02deskedit" && e.Name == "MUIVerb");
        }

        [Fact]
        public void Build_PopulatesSharedVerbStore()
        {
            var entries = MenuLayout.Build(Exe);
            string verbs = MenuLayout.ClassesRoot + "\\" + MenuLayout.VerbStore + @"\shell";

            Assert.Contains(entries, e => e.KeyPath == verbs + @"\01nomedia\command");
            Assert.Contains(entries, e => e.KeyPath == verbs + @"\02deskedit\command");
        }

        [Fact]
        public void Build_CommandsQuoteExeAndPassFolderViaPercentV()
        {
            var entries = MenuLayout.Build(Exe);
            var commands = entries
                .Where(e => e.Name == null && e.KeyPath.EndsWith(@"\command"))
                .Select(e => e.Value)
                .ToList();

            // The settings verb opens a window and takes no folder argument.
            var folderCommands = commands.Where(c => !c.EndsWith(" settings")).ToList();

            // Three verbs (nomedia, deskedit, settings), once per target and once in
            // the shared store.
            Assert.Equal(12, commands.Count);
            Assert.All(commands, c => Assert.StartsWith("\"" + Exe + "\"", c));
            Assert.All(folderCommands, c => Assert.EndsWith("\"%V\"", c));
            Assert.Equal(4, commands.Count - folderCommands.Count);
            Assert.Contains(commands, c => c.Contains(" nomedia "));
            Assert.Contains(commands, c => c.Contains(" deskedit "));
        }

        [Fact]
        public void BuildCommand_ProducesQuotedInvocation()
        {
            Assert.Equal(
                "\"C:\\Tools\\FolderTools.exe\" nomedia \"%V\"",
                MenuLayout.BuildCommand(@"C:\Tools\FolderTools.exe", "nomedia"));
        }

        [Fact]
        public void Build_WithoutExecutable_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => MenuLayout.Build("  "));
        }
    }
}
