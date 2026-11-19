using System;

namespace FolderTools.Core
{
    /// <summary>Normalises folder paths so two spellings of the same folder compare equal.</summary>
    public static class PathUtil
    {
        /// <summary>Trims whitespace and surrounding quotes, collapses a trailing separator.</summary>
        public static string NormalizeForComparison(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string value = path!.Trim().Trim('"');

            // Normalise separators so "/" and "\" spellings compare equal.
            value = value.Replace('/', '\\');

            // Drop a single trailing separator, but keep the root of a drive ("D:\").
            if (value.Length > 3 && value.EndsWith("\\", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
            }

            return value;
        }

        public static bool AreSameFolder(string? left, string? right)
        {
            return string.Equals(
                NormalizeForComparison(left),
                NormalizeForComparison(right),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Returns the folder path with exactly one trailing backslash, as written into .nomedia.</summary>
        public static string WithTrailingBackslash(string path)
        {
            string value = path.Trim().Trim('"').Replace('/', '\\');
            if (value.EndsWith("\\", StringComparison.Ordinal))
            {
                return value;
            }

            return value + "\\";
        }
    }
}
