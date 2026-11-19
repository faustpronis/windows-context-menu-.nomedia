using System;
using System.IO;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class AppPathsTests
    {
        [Fact]
        public void ToStored_MakesPathInsideInstallationRelative()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "ft-apppaths");

            string stored = AppPaths.ToStored(baseDir, Path.Combine(baseDir, "icons", "a.ico"));

            Assert.Equal(Path.Combine("icons", "a.ico"), stored);
        }

        [Fact]
        public void ToStored_KeepsAbsolutePathOutsideInstallation()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "ft-apppaths");
            string outside = Path.Combine(Path.GetTempPath(), "elsewhere", "a.ico");

            string stored = AppPaths.ToStored(baseDir, outside);

            Assert.True(Path.IsPathRooted(stored));
        }

        [Fact]
        public void Resolve_RebuildsPathFromRelativeValue()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "ft-apppaths");

            string full = AppPaths.Resolve(baseDir, Path.Combine("icons", "a.ico"));

            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "icons", "a.ico")), Path.GetFullPath(full));
        }

        [Fact]
        public void Resolve_KeepsAbsoluteValue()
        {
            string absolute = Path.Combine(Path.GetTempPath(), "abs", "a.ico");

            Assert.Equal(absolute, AppPaths.Resolve(@"C:\install", absolute));
        }

        [Fact]
        public void Resolve_EmptyForBlankInput()
        {
            Assert.Equal(string.Empty, AppPaths.Resolve(@"C:\install", null));
            Assert.Equal(string.Empty, AppPaths.Resolve(@"C:\install", "  "));
        }

        [Fact]
        public void IsInside_TrueOnlyForRelativeValues()
        {
            Assert.True(AppPaths.IsInside(@"C:\install", @"icons\a.ico"));
            Assert.False(AppPaths.IsInside(@"C:\install", @"C:\other\a.ico"));
            Assert.False(AppPaths.IsInside(@"C:\install", null));
        }

        [Fact]
        public void IconsDirectory_SitsUnderBase()
        {
            Assert.Equal(
                Path.Combine(@"C:\install", "icons"),
                AppPaths.IconsDirectory(@"C:\install"));
        }

        [Fact]
        public void SettingsPath_SitsNextToExe()
        {
            Assert.Equal(
                Path.Combine(@"C:\install", SettingsStore.FileName),
                AppPaths.SettingsPath(@"C:\install"));
        }
    }
}
