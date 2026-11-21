using System;
using System.IO;

namespace FolderTools.Core
{
    /// <summary>Normalises folder paths so two spellings of the same folder compare equal.</summary>
    public static class PathUtil
    {
        /// <summary>
        /// Explorer may pass a drive root as "M:". In Windows that spelling is
        /// drive-relative, so make it an explicit root before checking or using it.
        /// </summary>
        public static string NormalizeDriveRoot(string? path)
        {
            string value = (path ?? string.Empty).Trim().Trim('"');
            if (value.Length == 2 && char.IsLetter(value[0]) && value[1] == ':')
            {
                return value + "\\";
            }

            return value;
        }

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

        /// <summary>
        /// File or folder name without its directory. Both separators are honoured on
        /// every platform, because the shell hands over Windows paths while tests and
        /// tooling may run elsewhere, where Path.GetFileName only splits on '/'.
        /// </summary>
        public static string GetLeafName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string value = path!.Trim().Trim('"').TrimEnd('\\', '/');
            int separator = value.LastIndexOfAny(new[] { '\\', '/' });
            return separator >= 0 ? value.Substring(separator + 1) : value;
        }

        /// <summary>Directory part of a path, or an empty string when there is none.</summary>
        public static string GetDirectoryName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string value = path!.Trim().Trim('"').TrimEnd('\\', '/');
            int separator = value.LastIndexOfAny(new[] { '\\', '/' });

            // Keep "D:\" intact: the separator at index 2 belongs to the drive root.
            if (separator < 0)
            {
                return string.Empty;
            }

            if (separator == 2 && value.Length > 1 && value[1] == ':')
            {
                return value.Substring(0, 3);
            }

            return value.Substring(0, separator);
        }

        /// <summary>Joins a folder and a leaf name with a single backslash.</summary>
        public static string Combine(string folder, string name)
        {
            string trimmed = (folder ?? string.Empty).TrimEnd('\\', '/');
            return trimmed + "\\" + name;
        }

        /// <summary>
        /// Converts a logical Windows-style path to the separator the running platform
        /// uses for file IO. On Windows this is a no-op; elsewhere it lets the same code
        /// path be exercised by tests and tooling, where '\' is a legal file name
        /// character rather than a separator.
        /// </summary>
        public static string ToNative(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.DirectorySeparatorChar == '\\')
            {
                return path;
            }

            return path.Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}
