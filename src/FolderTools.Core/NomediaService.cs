using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FolderTools.Core
{
    public enum NomediaOutcome
    {
        /// <summary>The file did not exist and was created.</summary>
        Created,

        /// <summary>The file existed for another folder, so a new block was appended.</summary>
        Appended,

        /// <summary>The file already contained a block for this folder; nothing changed.</summary>
        AlreadyPresent
    }

    /// <summary>Outcome of <see cref="NomediaService.Ensure"/> plus the block that describes the folder.</summary>
    public sealed class NomediaResult
    {
        public NomediaResult(NomediaOutcome outcome, string block)
        {
            Outcome = outcome;
            Block = block;
        }

        public NomediaOutcome Outcome { get; }

        /// <summary>The two lines for this folder, whether they were written or already present.</summary>
        public string Block { get; }
    }

    /// <summary>
    /// Creates or updates a .nomedia file. The file records one block per folder:
    /// a header line with the timestamp and a line with the folder path.
    /// </summary>
    public static class NomediaService
    {
        public const string FileName = ".nomedia";
        private const string CrLf = "\r\n";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Builds the two lines (with trailing newline) that describe one folder.</summary>
        public static string BuildBlock(DateTime when, string folderPath)
        {
            return NomediaFormatter.FormatTimestamp(when) + CrLf
                 + PathUtil.WithTrailingBackslash(folderPath) + CrLf;
        }

        /// <summary>Creates or updates the .nomedia file inside <paramref name="folderPath"/>.</summary>
        public static NomediaResult Ensure(string folderPath, DateTime when)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("Folder path must not be empty.", nameof(folderPath));
            }

            string file = Path.Combine(folderPath, FileName);
            string block = BuildBlock(when, folderPath);
            string folderLine = PathUtil.WithTrailingBackslash(folderPath);

            if (!File.Exists(file))
            {
                File.WriteAllText(file, block, Utf8NoBom);
                return new NomediaResult(NomediaOutcome.Created, block);
            }

            string existing = File.ReadAllText(file);
            if (ContainsFolderLine(existing, folderLine))
            {
                return new NomediaResult(NomediaOutcome.AlreadyPresent, block);
            }

            var builder = new StringBuilder(existing);
            if (existing.Length > 0)
            {
                if (!existing.EndsWith("\n", StringComparison.Ordinal))
                {
                    builder.Append(CrLf);
                }

                // Separate the new block from the previous one with a blank line.
                builder.Append(CrLf);
            }

            builder.Append(block);

            File.WriteAllText(file, builder.ToString(), Utf8NoBom);
            return new NomediaResult(NomediaOutcome.Appended, block);
        }

        private static bool ContainsFolderLine(string content, string folderLine)
        {
            string wanted = PathUtil.NormalizeForComparison(folderLine);
            foreach (string line in SplitLines(content))
            {
                // Hand-edited files may lack the trailing separator, so compare normalised forms.
                if (string.Equals(
                        PathUtil.NormalizeForComparison(line),
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> SplitLines(string content)
        {
            return content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
    }
}
