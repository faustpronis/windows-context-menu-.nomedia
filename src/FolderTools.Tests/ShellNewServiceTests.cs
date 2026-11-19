using System;
using System.Collections.Generic;
using System.IO;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class ShellNewServiceTests
    {
        private static readonly DateTime When = new DateTime(2026, 11, 15, 6, 39, 28);

        [Fact]
        public void CreateFromPlaceholder_WritesNomediaInSameFolder()
        {
            string folder = NewFolder();
            try
            {
                string placeholder = Path.Combine(folder, "Новый .nomedia.nomedia");
                File.WriteAllText(placeholder, string.Empty);

                NomediaResult result = ShellNewService.CreateFromPlaceholder(placeholder, When);

                Assert.Equal(NomediaOutcome.Created, result.Outcome);
                string nomedia = Path.Combine(folder, NomediaService.FileName);
                Assert.True(File.Exists(nomedia));
                Assert.Contains("06:39:28  15.11.2026 Вс", File.ReadAllText(nomedia));
            }
            finally
            {
                Delete(folder);
            }
        }

        [Fact]
        public void CreateFromPlaceholder_RemovesThePlaceholder()
        {
            string folder = NewFolder();
            try
            {
                string placeholder = Path.Combine(folder, "Новый файл.nomedia");
                File.WriteAllText(placeholder, "placeholder");

                ShellNewService.CreateFromPlaceholder(placeholder, When);

                Assert.False(File.Exists(placeholder));
            }
            finally
            {
                Delete(folder);
            }
        }

        [Fact]
        public void CreateFromPlaceholder_KeepsFileWhenPlaceholderIsNomediaItself()
        {
            string folder = NewFolder();
            try
            {
                // A shell that generates the exact name would otherwise delete the file
                // the service just wrote.
                string placeholder = Path.Combine(folder, NomediaService.FileName);

                ShellNewService.CreateFromPlaceholder(placeholder, When);

                Assert.True(File.Exists(placeholder));
                Assert.NotEmpty(File.ReadAllText(placeholder));
            }
            finally
            {
                Delete(folder);
            }
        }

        [Fact]
        public void CreateFromPlaceholder_AppendsWhenNomediaExistsForAnotherFolder()
        {
            string folder = NewFolder();
            try
            {
                File.WriteAllText(
                    Path.Combine(folder, NomediaService.FileName),
                    "12:00:00  01.01.2020 Ср\r\nD:\\OTHER\\\r\n");

                NomediaResult result = ShellNewService.CreateFromPlaceholder(
                    Path.Combine(folder, "Новый.nomedia"),
                    When);

                Assert.Equal(NomediaOutcome.Appended, result.Outcome);
                Assert.Contains(@"D:\OTHER\", File.ReadAllText(Path.Combine(folder, NomediaService.FileName)));
            }
            finally
            {
                Delete(folder);
            }
        }

        [Fact]
        public void CreateFromPlaceholder_RejectsEmptyPath()
        {
            Assert.Throws<ArgumentException>(() => ShellNewService.CreateFromPlaceholder("  ", When));
        }

        [Fact]
        public void CreateFromPlaceholder_RejectsPathWithoutFolder()
        {
            Assert.Throws<ArgumentException>(() => ShellNewService.CreateFromPlaceholder("file.nomedia", When));
        }

        [Fact]
        public void CreateFromPlaceholder_DeletesThroughInjectedAction()
        {
            string folder = NewFolder();
            try
            {
                var deleted = new List<string>();
                string placeholder = Path.Combine(folder, "Новый.nomedia");

                ShellNewService.CreateFromPlaceholder(placeholder, When, deleted.Add);

                Assert.Equal(new[] { placeholder }, deleted);
            }
            finally
            {
                Delete(folder);
            }
        }

        private static string NewFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), "foldertools-shellnew-" + Guid.NewGuid().ToString("N"));
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
