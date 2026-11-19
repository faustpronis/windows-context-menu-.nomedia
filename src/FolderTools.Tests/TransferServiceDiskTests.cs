using System.IO;
using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    /// <summary>
    /// Exercises the real file system wiring rather than the injectable one, so a
    /// separator or path-joining mistake cannot slip through on a non-Windows host.
    /// </summary>
    public class TransferServiceDiskTests : System.IDisposable
    {
        private readonly string _root;

        public TransferServiceDiskTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "foldertools-transfer-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "src"));
            Directory.CreateDirectory(Path.Combine(_root, "dst"));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        private string Native(string relative)
        {
            return Path.Combine(_root, relative);
        }

        private string WriteSource(string name, string content)
        {
            string path = Native(Path.Combine("src", name));
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Copy_PutsFileInDestinationAndKeepsSource()
        {
            string source = WriteSource("a.txt", "hello");

            TransferResult result = new TransferService().Transfer(TransferAction.Copy, Native("dst"), new[] { source });

            Assert.False(result.HasErrors);
            Assert.True(File.Exists(Native(Path.Combine("dst", "a.txt"))));
            Assert.True(File.Exists(source));
        }

        [Fact]
        public void Copy_SecondTimeCreatesNumberedCopy()
        {
            string source = WriteSource("a.txt", "hello");
            var service = new TransferService();

            service.Transfer(TransferAction.Copy, Native("dst"), new[] { source });
            TransferResult second = service.Transfer(TransferAction.Copy, Native("dst"), new[] { source });

            Assert.False(second.HasErrors);
            Assert.True(second.Items.Single().Renamed);
            Assert.Equal(2, Directory.GetFiles(Native("dst")).Length);
        }

        [Fact]
        public void Move_RemovesSourceFile()
        {
            string source = WriteSource("b.txt", "world");

            TransferResult result = new TransferService().Transfer(TransferAction.Move, Native("dst"), new[] { source });

            Assert.False(result.HasErrors);
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(Native(Path.Combine("dst", "b.txt"))));
        }

        [Fact]
        public void Copy_FolderCopiesNestedContent()
        {
            string folder = Native(Path.Combine("src", "sub"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "inner.txt"), "x");

            TransferResult result = new TransferService().Transfer(TransferAction.Copy, Native("dst"), new[] { folder });

            Assert.False(result.HasErrors);
            Assert.True(File.Exists(Native(Path.Combine("dst", "sub", "inner.txt"))));
        }

        [Fact]
        public void Copy_CreatesMissingDestinationFolder()
        {
            string source = WriteSource("c.txt", "x");
            string target = Native(Path.Combine("deep", "nested"));

            TransferResult result = new TransferService().Transfer(TransferAction.Copy, target, new[] { source });

            Assert.False(result.HasErrors);
            Assert.True(File.Exists(Path.Combine(target, "c.txt")));
        }

        [Fact]
        public void Copy_MissingSourceReportsError()
        {
            TransferResult result = new TransferService().Transfer(
                TransferAction.Copy,
                Native("dst"),
                new[] { Path.Combine(_root, "src", "gone.txt") });

            Assert.True(result.HasErrors);
        }

        [Fact]
        public void Copy_DoesNotTouchOtherFiles()
        {
            string a = WriteSource("a.txt", "a");
            string b = WriteSource("b.txt", "b");
            var service = new TransferService();

            service.Transfer(TransferAction.Copy, Native("dst"), new[] { a });
            service.Transfer(TransferAction.Copy, Native("dst"), new[] { b });

            Assert.Equal(2, Directory.GetFiles(Native("dst")).Length);
            Assert.Equal(2, Directory.GetFiles(Native("src")).Length);
        }
    }
}
