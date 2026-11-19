using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    /// <summary>
    /// The shell filter and the in-app routing must agree, so a verb is never shown for
    /// a file the app would then refuse to transfer.
    /// </summary>
    public class NamePatternTests
    {
        [Theory]
        [InlineData("*.txt", @"C:\a\b.txt", true)]
        [InlineData("*.txt", @"C:\a\b.TXT", true)]
        [InlineData("*.txt", @"C:\a\b.jpg", false)]
        [InlineData("*.txt", @"C:\a\archive.tar.txt", true)]
        [InlineData("txt", @"C:\a\b.txt", true)]
        [InlineData("*", @"C:\a\README", true)]
        [InlineData("*", @"C:\a\b.txt", true)]
        public void ExtensionPatterns_MatchAgainstExtension(string pattern, string path, bool expected)
        {
            Assert.Equal(expected, ExtensionPattern.IsMatchName(pattern, path));
        }

        [Theory]
        [InlineData("фото!.jpg", @"C:\a\фото1.jpg", true)]
        [InlineData("фото!.jpg", @"C:\a\фотоA.jpg", true)]
        [InlineData("фото!.jpg", @"C:\a\фото10.jpg", false)]
        [InlineData("фото!.jpg", @"C:\a\снимок1.jpg", false)]
        [InlineData("фото*.jpg", @"C:\a\фото10.jpg", true)]
        [InlineData("фото*.jpg", @"C:\a\фото10.png", false)]
        public void NamePatterns_MatchAgainstWholeName(string pattern, string path, bool expected)
        {
            Assert.Equal(expected, ExtensionPattern.IsMatchName(pattern, path));
        }

        [Fact]
        public void FolderToken_MatchesOnlyDirectories()
        {
            Assert.True(ExtensionPattern.IsMatchName(ExtensionPattern.FolderToken, @"C:\Photos", true));
            Assert.False(ExtensionPattern.IsMatchName(ExtensionPattern.FolderToken, @"C:\Photos", false));
        }

        [Fact]
        public void FilePatterns_NeverMatchDirectories()
        {
            Assert.False(ExtensionPattern.IsMatchName("*.txt", @"C:\folder.txt", true));
            Assert.False(ExtensionPattern.IsMatchName("*", @"C:\anything", true));
        }

        [Fact]
        public void EmptyPattern_MatchesNothing()
        {
            Assert.False(ExtensionPattern.IsMatchName("", @"C:\a.txt"));
            Assert.False(ExtensionPattern.IsMatchName("   ", @"C:\a.txt"));
            Assert.False(ExtensionPattern.IsMatchName(null, @"C:\a.txt"));
        }

        [Fact]
        public void Destination_MatchesPath_UsesAnyPattern()
        {
            var destination = new Destination("d", "Photos", @"D:\P", new[] { "*.txt", "фото!.jpg" });

            Assert.True(destination.MatchesPath(@"C:\a\b.txt"));
            Assert.True(destination.MatchesPath(@"C:\a\фото7.jpg"));
            Assert.False(destination.MatchesPath(@"C:\a\фото77.jpg"));
        }
    }
}
