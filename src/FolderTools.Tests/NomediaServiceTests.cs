using System;
using System.IO;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class NomediaServiceTests : IDisposable
    {
        private readonly string _root;

        public NomediaServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "foldertools-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch
            {
                // best effort cleanup
            }
        }

        private string NewFolder(string name)
        {
            string path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static string Read(string folder)
        {
            return File.ReadAllText(Path.Combine(folder, NomediaService.FileName));
        }

        [Fact]
        public void Ensure_CreatesFileWithTimestampAndPath()
        {
            string folder = NewFolder("one");
            var when = new DateTime(2026, 11, 18, 18, 53, 23);

            NomediaResult result = NomediaService.Ensure(folder, when);

            Assert.Equal(NomediaOutcome.Created, result.Outcome);
            string content = Read(folder);
            Assert.Equal(
                "18:53:23  18.11.2026 Ср\r\n" + PathUtil.WithTrailingBackslash(folder) + "\r\n",
                content);
        }

        [Fact]
        public void Ensure_WritesUtf8WithoutBomAndCrlf()
        {
            string folder = NewFolder("bom");
            NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 1, 2, 3));

            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, NomediaService.FileName));
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Contains("\r\n", System.Text.Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public void Ensure_SecondRunForSameFolder_DoesNothing()
        {
            string folder = NewFolder("same");
            NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 10, 0, 0));
            string first = Read(folder);

            NomediaResult result = NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 11, 30, 0));

            Assert.Equal(NomediaOutcome.AlreadyPresent, result.Outcome);
            Assert.Equal(first, Read(folder));
        }

        [Fact]
        public void Ensure_SecondRunForOtherFolder_AppendsBlockAfterBlankLine()
        {
            string first = NewFolder("first");
            string second = NewFolder("second");

            // Simulate a .nomedia that already records a different folder
            // (for example the folder was renamed or the file was copied).
            File.WriteAllText(
                Path.Combine(second, NomediaService.FileName),
                "10:00:00  18.11.2026 Ср\r\n" + PathUtil.WithTrailingBackslash(first) + "\r\n");

            NomediaResult result = NomediaService.Ensure(second, new DateTime(2026, 11, 18, 12, 0, 0));

            Assert.Equal(NomediaOutcome.Appended, result.Outcome);
            string content = Read(second);
            string expected =
                "10:00:00  18.11.2026 Ср\r\n" + PathUtil.WithTrailingBackslash(first) + "\r\n" +
                "\r\n" +
                "12:00:00  18.11.2026 Ср\r\n" + PathUtil.WithTrailingBackslash(second) + "\r\n";
            Assert.Equal(expected, content);
        }

        [Fact]
        public void Ensure_DetectsExistingPathEvenWithDifferentCaseAndSlash()
        {
            string folder = NewFolder("case");
            string folderLine = PathUtil.WithTrailingBackslash(folder);

            // Same folder, but spelled with a different case and separators.
            string alternative = folderLine.ToUpperInvariant();
            File.WriteAllText(
                Path.Combine(folder, NomediaService.FileName),
                "10:00:00  18.11.2026 Ср\r\n" + alternative + "\r\n");

            NomediaResult result = NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 23, 0, 0));

            Assert.Equal(NomediaOutcome.AlreadyPresent, result.Outcome);
        }

        [Fact]
        public void Ensure_AppendsWhenExistingFileHasNoTrailingNewline()
        {
            string folder = NewFolder("noeol");
            File.WriteAllText(Path.Combine(folder, NomediaService.FileName), "junk line without newline");

            NomediaResult result = NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 9, 9, 9));

            Assert.Equal(NomediaOutcome.Appended, result.Outcome);
            string content = Read(folder);
            Assert.StartsWith("junk line without newline\r\n\r\n09:09:09", content);
        }

        [Fact]
        public void Ensure_EmptyFile_DoesNotStartWithBlankLine()
        {
            string folder = NewFolder("empty");
            File.WriteAllText(Path.Combine(folder, NomediaService.FileName), string.Empty);

            NomediaResult result = NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 9, 9, 9));

            Assert.Equal(NomediaOutcome.Appended, result.Outcome);
            Assert.StartsWith("09:09:09  18.11.2026 Ср\r\n", Read(folder));
        }

        [Fact]
        public void Ensure_MatchesExistingPathWithoutTrailingSeparator()
        {
            string folder = NewFolder("notrailing");
            File.WriteAllText(
                Path.Combine(folder, NomediaService.FileName),
                "10:00:00  18.11.2026 Ср\r\n" + folder + "\r\n");

            NomediaResult result = NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 9, 9, 9));

            Assert.Equal(NomediaOutcome.AlreadyPresent, result.Outcome);
        }

        [Fact]
        public void Ensure_ReturnsBlockThatMatchesWrittenFile()
        {
            string folder = NewFolder("block");

            NomediaResult result = NomediaService.Ensure(folder, new DateTime(2026, 11, 18, 9, 9, 9));

            Assert.Equal(
                "09:09:09  18.11.2026 Ср\r\n" + PathUtil.WithTrailingBackslash(folder) + "\r\n",
                result.Block);
            Assert.Equal(result.Block, Read(folder));
        }
    }
}
