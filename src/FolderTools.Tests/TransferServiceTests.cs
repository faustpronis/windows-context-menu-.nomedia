using System.Collections.Generic;
using System.IO;
using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class TransferServiceTests
    {
        /// <summary>In-memory file system, so routing and naming are tested without disk IO.</summary>
        private sealed class FakeFs
        {
            public HashSet<string> Files { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Directories { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            public List<string> Log { get; } = new List<string>();

            public TransferService Build()
            {
                return new TransferService(
                    path => Files.Contains(path),
                    path => Directories.Contains(path),
                    (from, to) => { Files.Add(to); Log.Add("copy " + from + " -> " + to); },
                    (from, to) => { Files.Remove(from); Files.Add(to); Log.Add("move " + from + " -> " + to); },
                    (from, to) => { Directories.Remove(from); Directories.Add(to); Log.Add("moveDir " + from + " -> " + to); },
                    (from, to) => { Directories.Add(to); Log.Add("copyDir " + from + " -> " + to); });
            }
        }

        [Fact]
        public void Transfer_CopiesFileIntoDestination()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"C:\src\a.txt");
            fs.Directories.Add(@"D:\dst");

            TransferResult result = fs.Build().Transfer(TransferAction.Copy, @"D:\dst", new[] { @"C:\src\a.txt" });

            Assert.False(result.HasErrors);
            Assert.Equal(@"D:\dst\a.txt", result.Items.Single().Target);
            Assert.Contains(@"D:\dst\a.txt", fs.Files);
            Assert.Contains(@"C:\src\a.txt", fs.Files);
        }

        [Fact]
        public void Transfer_MoveRemovesSource()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"C:\src\a.txt");
            fs.Directories.Add(@"D:\dst");

            fs.Build().Transfer(TransferAction.Move, @"D:\dst", new[] { @"C:\src\a.txt" });

            Assert.DoesNotContain(@"C:\src\a.txt", fs.Files);
            Assert.Contains(@"D:\dst\a.txt", fs.Files);
        }

        [Fact]
        public void Transfer_NameClash_AddsNumberSuffix()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"C:\src\a.txt");
            fs.Files.Add(@"D:\dst\a.txt");
            fs.Directories.Add(@"D:\dst");

            TransferResult result = fs.Build().Transfer(TransferAction.Copy, @"D:\dst", new[] { @"C:\src\a.txt" });

            Assert.Equal(@"D:\dst\a (2).txt", result.Items.Single().Target);
            Assert.True(result.Items.Single().Renamed);
            Assert.Contains(@"D:\dst\a.txt", fs.Files);
        }

        [Fact]
        public void Transfer_MultipleClashesIncrementSuffix()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"C:\src\a.txt");
            fs.Files.Add(@"D:\dst\a.txt");
            fs.Files.Add(@"D:\dst\a (2).txt");
            fs.Directories.Add(@"D:\dst");

            TransferResult result = fs.Build().Transfer(TransferAction.Copy, @"D:\dst", new[] { @"C:\src\a.txt" });

            Assert.Equal(@"D:\dst\a (3).txt", result.Items.Single().Target);
        }

        [Fact]
        public void Transfer_FolderCopyUsesDirectoryName()
        {
            var fs = new FakeFs();
            fs.Directories.Add(@"C:\src\Photos");
            fs.Directories.Add(@"D:\dst");

            TransferResult result = fs.Build().Transfer(TransferAction.Copy, @"D:\dst", new[] { @"C:\src\Photos" });

            Assert.Equal(@"D:\dst\Photos", result.Items.Single().Target);
            Assert.Contains(@"D:\dst\Photos", fs.Directories);
        }

        [Fact]
        public void Transfer_SkipsItemAlreadyInDestination()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"D:\dst\a.txt");
            fs.Directories.Add(@"D:\dst");

            TransferResult result = fs.Build().Transfer(TransferAction.Copy, @"D:\dst", new[] { @"D:\dst\a.txt" });

            Assert.True(result.HasErrors);
            Assert.Empty(result.Items);
            Assert.Empty(fs.Log);
        }

        [Fact]
        public void Transfer_MissingSource_ReportsError()
        {
            var fs = new FakeFs();
            fs.Directories.Add(@"D:\dst");

            TransferResult result = fs.Build().Transfer(TransferAction.Copy, @"D:\dst", new[] { @"C:\gone.txt" });

            Assert.True(result.HasErrors);
            Assert.Empty(result.Items);
        }

        [Fact]
        public void Transfer_CreatesDestinationFolderWhenMissing()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"C:\src\a.txt");

            TransferResult result = fs.Build().Transfer(TransferAction.Copy, @"D:\new\deep", new[] { @"C:\src\a.txt" });

            Assert.False(result.HasErrors);
            Assert.Contains(@"D:\new\deep\a.txt", fs.Files);
        }

        [Fact]
        public void Transfer_MixedSelection_TransfersEveryItem()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"C:\src\a.txt");
            fs.Files.Add(@"C:\src\b.jpg");
            fs.Directories.Add(@"D:\dst");

            TransferResult result = fs.Build().Transfer(
                TransferAction.Copy,
                @"D:\dst",
                new[] { @"C:\src\a.txt", @"C:\src\b.jpg" });

            Assert.False(result.HasErrors);
            Assert.Equal(2, result.Items.Count);
        }

        [Fact]
        public void Transfer_MoveStopsAtFirstFailureSoNothingIsLostSilently()
        {
            var fs = new FakeFs();
            fs.Files.Add(@"C:\src\a.txt");
            fs.Files.Add(@"C:\src\b.txt");
            fs.Directories.Add(@"D:\dst");

            TransferService service = new TransferService(
                fs.Files.Contains,
                fs.Directories.Contains,
                (f, t) => { },
                (f, t) =>
                {
                    if (f.EndsWith("b.txt"))
                    {
                        throw new IOException("locked");
                    }

                    fs.Files.Remove(f);
                    fs.Files.Add(t);
                },
                (f, t) => { },
                (f, t) => { });

            TransferResult result = service.Transfer(
                TransferAction.Move,
                @"D:\dst",
                new[] { @"C:\src\a.txt", @"C:\src\b.txt", @"C:\src\c.txt" });

            Assert.True(result.HasErrors);
            Assert.Single(result.Items);
        }

        [Fact]
        public void BuildAvailablePath_KeepsNameWhenFree()
        {
            var fs = new FakeFs();
            fs.Directories.Add(@"D:\dst");

            Assert.Equal(@"D:\dst\a.txt", fs.Build().BuildAvailablePath(@"D:\dst", "a.txt", false));
        }

        [Fact]
        public void Transfer_WithoutDestination_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => new TransferService().Transfer(TransferAction.Copy, "  ", new[] { @"C:\a.txt" }));
        }
    }
}
