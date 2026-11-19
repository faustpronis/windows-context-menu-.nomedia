using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class VersionUtilTests
    {
        [Theory]
        [InlineData("0.1.0", "0.1.0")]
        [InlineData("0.1.0+71623bc", "0.1.0")]
        [InlineData("1.2.3-beta+abc123", "1.2.3-beta")]
        [InlineData(" 0.1.0 ", "0.1.0")]
        public void Display_StripsSourceRevision(string informational, string expected)
        {
            Assert.Equal(expected, VersionUtil.Display(informational, "9.9.9"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Display_FallsBackToAssemblyVersion(string? informational)
        {
            Assert.Equal("0.1.0", VersionUtil.Display(informational, "0.1.0"));
        }

        [Fact]
        public void Display_WhenNothingUsable_ReturnsZero()
        {
            Assert.Equal("0.0.0", VersionUtil.Display(null, null));
        }
    }
}
