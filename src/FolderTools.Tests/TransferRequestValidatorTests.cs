using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class TransferRequestValidatorTests
    {
        private static Destination Make(params string[] patterns)
        {
            return new Destination("d", "Text", @"D:\Text", patterns);
        }

        [Fact]
        public void Validate_AcceptsMatchingFile()
        {
            Assert.Null(TransferRequestValidator.Validate(Make("*.txt"), new[] { @"C:\a.txt" }));
        }

        [Fact]
        public void Validate_RejectsNonMatchingFile()
        {
            string? message = TransferRequestValidator.Validate(Make("*.txt"), new[] { @"C:\a.jpg" });

            Assert.NotNull(message);
            Assert.Contains("a.jpg", message);
        }

        [Fact]
        public void Validate_ReportsMissingDestination()
        {
            Assert.NotNull(TransferRequestValidator.Validate(null, new[] { @"C:\a.txt" }));
        }

        [Fact]
        public void Validate_ReportsDestinationWithoutPath()
        {
            var destination = new Destination("d", "Text", "  ", new[] { "*.txt" });

            Assert.NotNull(TransferRequestValidator.Validate(destination, new[] { @"C:\a.txt" }));
        }

        [Fact]
        public void Validate_ReportsEmptySelection()
        {
            Assert.NotNull(TransferRequestValidator.Validate(Make("*.txt"), new string[0]));
        }

        [Fact]
        public void Validate_FolderTokenRequiresDirectory()
        {
            var folders = Make(ExtensionPattern.FolderToken);

            Assert.Null(TransferRequestValidator.Validate(folders, new[] { @"C:\Photos" }, _ => true));
            Assert.NotNull(TransferRequestValidator.Validate(folders, new[] { @"C:\Photos" }, _ => false));
        }

        [Fact]
        public void Validate_ChecksEveryItem()
        {
            string? message = TransferRequestValidator.Validate(
                Make("*.txt"),
                new[] { @"C:\a.txt", @"C:\b.jpg" });

            Assert.NotNull(message);
            Assert.Contains("b.jpg", message);
            Assert.DoesNotContain("a.txt", message);
        }
    }
}
