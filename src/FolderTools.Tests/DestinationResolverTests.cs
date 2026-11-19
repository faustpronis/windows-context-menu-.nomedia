using System.Collections.Generic;
using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class DestinationResolverTests
    {
        private static Destination Make(string id, string path, params string[] patterns)
        {
            return new Destination(id, id, path, patterns);
        }

        [Fact]
        public void Resolve_RoutesEachFileByItsOwnExtension()
        {
            var destinations = new[]
            {
                Make("txt", @"D:\Text", "*.txt"),
                Make("jpg", @"D:\Images", "*.jpg")
            };

            var matches = DestinationResolver.Resolve(destinations, new[] { @"C:\a.txt", @"C:\b.jpg" });

            Assert.Equal(2, matches.Count);
            Assert.Equal(new[] { @"C:\a.txt" }, matches[0].Files);
            Assert.Equal(new[] { @"C:\b.jpg" }, matches[1].Files);
        }

        [Fact]
        public void DestinationsFor_MixedSelection_YieldsEveryMatchingDestination()
        {
            var destinations = new[]
            {
                Make("txt", @"D:\Text", "*.txt"),
                Make("jpg", @"D:\Images", "*.jpg"),
                Make("pdf", @"D:\Docs", "*.pdf")
            };

            var result = DestinationResolver.DestinationsFor(destinations, new[] { @"C:\a.txt", @"C:\b.jpg" });

            Assert.Equal(new[] { "txt", "jpg" }, result.Select(d => d.Id));
        }

        [Fact]
        public void Resolve_SkipsDestinationWhenNoSelectedItemMatches()
        {
            var destinations = new[] { Make("pdf", @"D:\Docs", "*.pdf") };

            Assert.Empty(DestinationResolver.Resolve(destinations, new[] { @"C:\a.txt" }));
        }

        [Fact]
        public void Resolve_SkipsDisabledDestinations()
        {
            var destinations = new[] { Make("empty", @"D:\A") };

            Assert.Empty(DestinationResolver.Resolve(destinations, new[] { @"C:\a.txt" }));
        }

        [Fact]
        public void Resolve_WildcardMatchesFilesWithoutExtension()
        {
            var destinations = new[] { Make("all", @"D:\All", "*") };

            Assert.Single(DestinationResolver.Resolve(destinations, new[] { @"C:\README" }));
        }

        [Fact]
        public void Resolve_FolderTokenMatchesOnlyDirectories()
        {
            var destinations = new[] { Make("folders", @"D:\Folders", ExtensionPattern.FolderToken) };

            var asFile = DestinationResolver.Resolve(destinations, new[] { @"C:\Photos" }, _ => false);
            var asFolder = DestinationResolver.Resolve(destinations, new[] { @"C:\Photos" }, _ => true);

            Assert.Empty(asFile);
            Assert.Single(asFolder);
        }

        [Fact]
        public void Resolve_FilePatternsDoNotMatchDirectories()
        {
            var destinations = new[] { Make("txt", @"D:\Text", "*.txt") };

            Assert.Empty(DestinationResolver.Resolve(destinations, new[] { @"C:\folder.txt" }, _ => true));
        }

        [Theory]
        [InlineData(@"C:\a\b.txt", ".txt")]
        [InlineData(@"C:\a\b.tar.gz", ".gz")]
        [InlineData(@"C:\a\noext", "")]
        [InlineData(@"C:\a\.gitignore", "")]
        [InlineData(@"C:\a\trailing.", "")]
        [InlineData("", "")]
        public void GetExtension_HandlesEdgeCases(string path, string expected)
        {
            Assert.Equal(expected, DestinationResolver.GetExtension(path));
        }

        [Fact]
        public void Resolve_IgnoresBlankItems()
        {
            var destinations = new[] { Make("all", @"D:\All", "*") };

            var matches = DestinationResolver.Resolve(destinations, new[] { @"C:\a.txt", "  " });

            Assert.Single(matches.Single().Files);
        }
    }
}
