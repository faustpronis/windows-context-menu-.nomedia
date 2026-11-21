using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class PathUtilTests
    {
        [Theory]
        [InlineData("M:", @"M:\")]
        [InlineData("  \"m:\"  ", @"m:\")]
        [InlineData(@"M:\", @"M:\")]
        [InlineData(@"M:\Folder", @"M:\Folder")]
        public void NormalizeDriveRoot_MakesDriveRootExplicit(string input, string expected)
        {
            Assert.Equal(expected, PathUtil.NormalizeDriveRoot(input));
        }

        [Theory]
        [InlineData(@"D:\SHARE\SYNCTHING", @"d:\share\syncthing\")]
        [InlineData(@"D:\SHARE\SYNCTHING\", @"D:\SHARE\SYNCTHING")]
        [InlineData("  \"D:\\Share\\SyncThing\"  ", @"D:/SHARE/SYNCTHING")]
        [InlineData(@"D:/SHARE/SYNCTHING", @"D:\share\syncthing")]
        public void AreSameFolder_IgnoresCaseSeparatorsAndTrailingSlash(string left, string right)
        {
            Assert.True(PathUtil.AreSameFolder(left, right));
        }

        [Fact]
        public void AreSameFolder_DifferentFoldersAreNotEqual()
        {
            Assert.False(PathUtil.AreSameFolder(@"D:\A", @"D:\B"));
        }

        [Theory]
        [InlineData(@"D:\SHARE\SYNCTHING", @"D:\SHARE\SYNCTHING\")]
        [InlineData(@"D:\SHARE\SYNCTHING\", @"D:\SHARE\SYNCTHING\")]
        [InlineData("D:/SHARE/SYNCTHING", @"D:\SHARE\SYNCTHING\")]
        public void WithTrailingBackslash_AlwaysEndsWithSingleSeparator(string input, string expected)
        {
            Assert.Equal(expected, PathUtil.WithTrailingBackslash(input));
        }

        [Fact]
        public void WithTrailingBackslash_KeepsDriveRoot()
        {
            Assert.Equal(@"D:\", PathUtil.WithTrailingBackslash(@"D:\"));
        }
    }
}
