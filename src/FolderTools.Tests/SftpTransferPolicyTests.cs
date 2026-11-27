using System;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public sealed class SftpTransferPolicyTests
    {
        [Theory]
        [InlineData("MyFile.jpg", "MyFile-conflict-2026-11-26_22-03-58.jpg")]
        [InlineData("archive.tar.gz", "archive.tar-conflict-2026-11-26_22-03-58.gz")]
        [InlineData("README", "README-conflict-2026-11-26_22-03-58")]
        [InlineData(".env", ".env-conflict-2026-11-26_22-03-58")]
        public void ConflictName_UsesTimestampAndPreservesExtension(string input, string expected)
        {
            Assert.Equal(expected, SftpConflictNaming.CreateName(input, new DateTime(2026, 11, 26, 22, 3, 58)));
        }

        [Fact]
        public void ConflictName_AddsNumberForFurtherCollisions()
        {
            Assert.Equal("file-conflict-2026-11-26_22-03-58-2.txt",
                SftpConflictNaming.CreateName("file.txt", new DateTime(2026, 11, 26, 22, 3, 58), 2));
        }
    }
}
