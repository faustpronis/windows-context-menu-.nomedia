using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class ExtensionPatternTests
    {
        [Theory]
        [InlineData("*.txt", ".txt", true)]
        [InlineData("*.txt", ".TXT", true)]
        [InlineData("*.txt", ".jpg", false)]
        [InlineData(".txt", ".txt", true)]
        [InlineData("txt", ".txt", true)]
        [InlineData("*txt", ".txt", true)]
        [InlineData("*.txt", "", false)]
        public void IsMatch_BasicPatterns(string pattern, string extension, bool expected)
        {
            Assert.Equal(expected, ExtensionPattern.IsMatch(pattern, extension));
        }

        [Theory]
        [InlineData("photo!.jpg", ".jpg", false)]
        [InlineData("*.jp!", ".jpg", true)]
        [InlineData("*.jp!", ".jp", false)]
        [InlineData("*.jpe!", ".jpg", false)]
        [InlineData("*.jpe!", ".jpeg", true)]
        [InlineData("!.txt", ".a.txt", false)]
        public void IsMatch_ExclamationMatchesExactlyOneCharacter(string pattern, string extension, bool expected)
        {
            Assert.Equal(expected, ExtensionPattern.IsMatch(pattern, extension));
        }

        [Theory]
        [InlineData("*", ".anything", true)]
        [InlineData("*", "", true)]
        [InlineData("**", ".x", true)]
        public void IsMatch_StarMatchesEverything(string pattern, string extension, bool expected)
        {
            Assert.Equal(expected, ExtensionPattern.IsMatch(pattern, extension));
        }

        [Fact]
        public void IsMatch_RegexMetacharactersAreLiteral()
        {
            // A dot in the pattern must not act as "any character".
            Assert.False(ExtensionPattern.IsMatch("*.t.t", ".txt"));
            Assert.True(ExtensionPattern.IsMatch("*.t.t", ".t.t"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void IsMatch_EmptyPatternNeverMatches(string? pattern)
        {
            Assert.False(ExtensionPattern.IsMatch(pattern, ".txt"));
        }

        [Theory]
        [InlineData("*", null)]
        [InlineData("*.txt", "*.txt")]
        [InlineData("*.jp!", "*.jp?")]
        [InlineData("photo*.txt", "photo*.txt")]
        [InlineData(".TXT", ".txt")]
        public void ToShellWildcard_TranslatesToAqsSyntax(string pattern, string? expected)
        {
            Assert.Equal(expected, ExtensionPattern.ToShellWildcard(pattern));
        }

        [Theory]
        [InlineData("*.txt", "*.txt")]
        [InlineData("*.jp!", "*.jp?")]
        public void ToShellWildcard_IsAcceptedByShellMatching(string pattern, string shellPattern)
        {
            // The shell treats '?' as one character; our '!' must behave the same.
            Assert.Equal(
                ExtensionPattern.IsMatch(pattern, ".jpg"),
                System.Text.RegularExpressions.Regex.IsMatch(".jpg", "^" + shellPattern.Replace(".", "\\.").Replace("*", ".*").Replace("?", ".") + "$"));
        }
    }
}
