using System;
using System.IO;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class IconStoreTests
    {
        private static byte[] FakeIco(byte seed = 1)
        {
            var bytes = new byte[64];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }

            return bytes;
        }

        [Fact]
        public void Import_CopiesIconIntoIconsFolder()
        {
            string baseDir = NewFolder();
            string source = Path.Combine(baseDir, "pick.ico");
            try
            {
                File.WriteAllBytes(source, FakeIco());

                string stored = IconStore.Import(baseDir, "abc123", source);

                Assert.StartsWith(Path.Combine("icons", "abc123-"), stored);
                Assert.EndsWith(".ico", stored);
                Assert.True(File.Exists(Path.Combine(baseDir, stored)));
                Assert.Equal(FakeIco(), File.ReadAllBytes(Path.Combine(baseDir, stored)));
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Import_ReturnsRelativePathSoInstallationStaysMovable()
        {
            string baseDir = NewFolder();
            try
            {
                string source = Path.Combine(baseDir, "pick.ico");
                File.WriteAllBytes(source, FakeIco());

                string stored = IconStore.Import(baseDir, "id1", source);

                Assert.False(Path.IsPathRooted(stored));
                Assert.StartsWith("icons", stored);
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Import_CreatesIconsFolderWhenMissing()
        {
            string baseDir = NewFolder();
            try
            {
                string source = Path.Combine(baseDir, "pick.ico");
                File.WriteAllBytes(source, FakeIco());
                string icons = Path.Combine(baseDir, "icons");
                Assert.False(Directory.Exists(icons));

                IconStore.Import(baseDir, "id1", source);

                Assert.True(Directory.Exists(icons));
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Import_ChangesStoredPathWhenIconChanges()
        {
            string baseDir = NewFolder();
            try
            {
                string first = Path.Combine(baseDir, "a.ico");
                string second = Path.Combine(baseDir, "b.ico");
                File.WriteAllBytes(first, FakeIco(1));
                File.WriteAllBytes(second, FakeIco(9));

                string previous = IconStore.Import(baseDir, "same", first);
                string stored = IconStore.Import(baseDir, "same", second);

                Assert.NotEqual(previous, stored);
                Assert.Equal(FakeIco(9), File.ReadAllBytes(Path.Combine(baseDir, stored)));
                Assert.Equal(2, Directory.GetFiles(Path.Combine(baseDir, "icons")).Length);
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Import_KeepsSourceWhenItAlreadyIsTheTarget()
        {
            string baseDir = NewFolder();
            try
            {
                string source = Path.Combine(baseDir, "pick.ico");
                File.WriteAllBytes(source, FakeIco());
                string stored = IconStore.Import(baseDir, "x", source);
                string target = Path.Combine(baseDir, stored);

                // Re-picking the icon already in place must not truncate it.
                IconStore.Import(baseDir, "x", target);

                Assert.Equal(FakeIco(), File.ReadAllBytes(target));
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Import_ThrowsWhenSourceMissing()
        {
            string baseDir = NewFolder();
            try
            {
                Assert.Throws<FileNotFoundException>(
                    () => IconStore.Import(baseDir, "x", Path.Combine(baseDir, "nope.ico")));
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Import_SanitisesDestinationIdForFileName()
        {
            string baseDir = NewFolder();
            try
            {
                string source = Path.Combine(baseDir, "pick.ico");
                File.WriteAllBytes(source, FakeIco());

                string stored = IconStore.Import(baseDir, @"a/b:c*d", source);

                Assert.StartsWith(Path.Combine("icons", "a_b_c_d-"), stored);
                Assert.EndsWith(".ico", stored);
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Delete_RemovesIconInsideIconsFolder()
        {
            string baseDir = NewFolder();
            try
            {
                string source = Path.Combine(baseDir, "pick.ico");
                File.WriteAllBytes(source, FakeIco());
                string stored = IconStore.Import(baseDir, "id1", source);

                Assert.True(IconStore.Delete(baseDir, stored));
                Assert.False(File.Exists(Path.Combine(baseDir, stored)));
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Delete_RefusesPathOutsideIconsFolder()
        {
            string baseDir = NewFolder();
            try
            {
                string outside = Path.Combine(baseDir, "keep.ico");
                File.WriteAllBytes(outside, FakeIco());

                Assert.False(IconStore.Delete(baseDir, "keep.ico"));
                Assert.True(File.Exists(outside));
            }
            finally
            {
                Delete(baseDir);
            }
        }

        [Fact]
        public void Delete_RefusesAbsolutePath()
        {
            string baseDir = NewFolder();
            try
            {
                string outside = Path.Combine(baseDir, "keep.ico");
                File.WriteAllBytes(outside, FakeIco());

                Assert.False(IconStore.Delete(baseDir, outside));
                Assert.True(File.Exists(outside));
            }
            finally
            {
                Delete(baseDir);
            }
        }

        private static string NewFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), "foldertools-icons-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Delete(string folder)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
