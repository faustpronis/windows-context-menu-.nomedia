using System;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class SftpPermissionRulesTests
    {
        [Fact]
        public void ParseExtensions_NormalizesAndDeduplicates()
        {
            var result = SftpPermissionRules.ParseExtensions("*.SH; .py,sh\nphp,*.Tar.Gz");

            Assert.Equal(new[] { ".sh", ".py", ".php", ".tar.gz" }, result);
        }

        [Theory]
        [InlineData("../x")]
        [InlineData("*")]
        public void ParseExtensions_RejectsUnsafeOrAmbiguousValues(string value)
        {
            Assert.Throws<FormatException>(() => SftpPermissionRules.ParseExtensions(value));
        }

        [Theory]
        [InlineData("0644", 420)]
        [InlineData("755", 493)]
        [InlineData("0700", 448)]
        public void TryParseMode_AcceptsOctalPermissions(string text, short expected)
        {
            Assert.True(SftpPermissionRules.TryParseMode(text, out short mode));
            Assert.Equal(expected, mode);
        }

        [Theory]
        [InlineData("888")]
        [InlineData("1777")]
        [InlineData("06444")]
        [InlineData("")]
        public void TryParseMode_RejectsInvalidOrSpecialBits(string text)
        {
            Assert.False(SftpPermissionRules.TryParseMode(text, out _));
        }

        [Fact]
        public void ShouldApply_MatchesExtensionCaseInsensitively()
        {
            Assert.True(SftpPermissionRules.ShouldApply("/tmp/deploy.SH", new[] { ".sh" }));
            Assert.False(SftpPermissionRules.ShouldApply("/tmp/deploy.txt", new[] { ".sh" }));
            Assert.True(SftpPermissionRules.ShouldApply("/tmp/archive.tar.gz", new[] { ".tar.gz" }));
        }
    }
}
