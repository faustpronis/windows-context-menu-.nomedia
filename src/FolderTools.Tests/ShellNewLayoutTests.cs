using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class ShellNewLayoutTests
    {
        private const string Exe = @"C:\Tools\FolderTools\FolderTools.exe";

        [Fact]
        public void BuildShellNew_RegistersExtensionToProgId()
        {
            var entries = MenuLayout.BuildShellNew(Exe);
            var extension = entries.Single(e => e.KeyPath == @"Software\Classes\.nomedia" && e.Name == null);

            Assert.Equal("FolderTools.nomedia", extension.Value);
        }

        [Fact]
        public void BuildShellNew_PointsShellNewAtTheTool()
        {
            var entries = MenuLayout.BuildShellNew(Exe);
            var command = entries.Single(e => e.KeyPath == @"Software\Classes\FolderTools.nomedia\ShellNew" && e.Name == "Command");

            // The shell hands over the placeholder it generated; the tool replaces it.
            Assert.Equal("\"" + Exe + "\" shellnew \"%1\"", command.Value);
        }

        [Fact]
        public void BuildShellNew_UsesCommandInsteadOfNullFile()
        {
            var entries = MenuLayout.BuildShellNew(Exe);

            Assert.DoesNotContain(entries, e => e.Name == "NullFile");
        }

        [Fact]
        public void BuildShellNew_ProvidesLabelAndIcon()
        {
            var entries = MenuLayout.BuildShellNew(Exe);

            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\FolderTools.nomedia" && e.Name == null && e.Value.Length > 0);
            Assert.Contains(entries, e => e.KeyPath == @"Software\Classes\FolderTools.nomedia\DefaultIcon");
        }

        [Fact]
        public void BuildShellNew_RejectsEmptyExecutablePath()
        {
            Assert.Throws<System.ArgumentException>(() => MenuLayout.BuildShellNew("  "));
        }
    }
}
