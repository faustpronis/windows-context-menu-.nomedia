using System;
using System.Text;
using System.Text.RegularExpressions;

namespace FolderTools.Core
{
    /// <summary>
    /// Matches a file's extension against a user-entered wildcard pattern.
    ///
    /// The user syntax is deliberately tiny: <c>*</c> matches any run of characters
    /// and <c>!</c> matches exactly one character. Both are translated to regular
    /// expressions, and everything else is escaped so a stray dot or bracket in a
    /// pattern cannot change the meaning of the match.
    /// </summary>
    public static class ExtensionPattern
    {
        /// <summary>Matches every extension, including files without one.</summary>
        public const string All = "*";

        /// <summary>
        /// Pattern that matches directories only. A folder has no extension, so without
        /// this token there would be no way to route folders to a destination that
        /// should not also receive files.
        /// </summary>
        public const string FolderToken = "[папка]";

        /// <summary>English spelling of <see cref="FolderToken"/>, accepted as well.</summary>
        public const string FolderTokenEn = "[folder]";

        /// <summary>True when the pattern targets directories rather than extensions.</summary>
        public static bool IsFolderPattern(string? pattern)
        {
            string value = (pattern ?? string.Empty).Trim().ToLowerInvariant();
            return value == FolderToken || value == FolderTokenEn;
        }

        /// <summary>
        /// Accepts patterns with or without the leading dot and in any case, so
        /// "*.TXT", ".txt" and "*txt" all describe the same set.
        /// </summary>
        public static string Normalize(string? pattern)
        {
            if (pattern == null)
            {
                return string.Empty;
            }

            string value = pattern.Trim().ToLowerInvariant();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            if (value == FolderTokenEn)
            {
                return FolderToken;
            }

            if (IsFolderPattern(value))
            {
                return FolderToken;
            }

            // A bare "txt" is what people type; treat it as an extension, not a name.
            // Only do this when the pattern has no dot at all, so "photo*.txt" keeps
            // its shape instead of turning into ".photo*.txt".
            if (value.IndexOf('.') < 0 && value[0] != '*')
            {
                value = "." + value;
            }

            return value;
        }

        /// <summary>
        /// Returns true when <paramref name="extension"/> (".txt", or "" for none)
        /// satisfies the pattern. Directories only match the folder token.
        /// </summary>
        public static bool IsMatch(string? pattern, string? extension, bool isDirectory = false)
        {
            string normalized = Normalize(pattern);
            if (normalized.Length == 0)
            {
                return false;
            }

            if (normalized == FolderToken)
            {
                return isDirectory;
            }

            if (isDirectory)
            {
                return false;
            }

            string ext = (extension ?? string.Empty).Trim().ToLowerInvariant();
            if (ext.Length > 0 && ext[0] != '.')
            {
                ext = "." + ext;
            }

            return Regex.IsMatch(ext, "^" + ToRegex(normalized) + "$", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Returns true when a file name satisfies the pattern.
        ///
        /// Patterns that start with '*' or '.' describe an extension ("*.txt", ".txt",
        /// "txt") and are matched against the extension alone, so "*.txt" also matches
        /// "archive.tar.txt". Any other pattern constrains the name itself
        /// ("фото!.jpg") and is matched against the whole name. Keeping the two cases
        /// apart is what makes the menu and the actual routing agree, since the shell
        /// filter always sees the full name.
        /// </summary>
        public static bool IsMatchName(string? pattern, string? path, bool isDirectory = false)
        {
            string normalized = Normalize(pattern);
            if (normalized.Length == 0)
            {
                return false;
            }

            if (normalized == FolderToken)
            {
                return isDirectory;
            }

            if (isDirectory)
            {
                return false;
            }

            string name = PathUtil.GetLeafName(path).ToLowerInvariant();
            bool extensionOnly = normalized[0] == '*' || normalized[0] == '.';
            string target = extensionOnly ? ExtensionOf(name) : name;

            return Regex.IsMatch(target, "^" + ToRegex(normalized) + "$", RegexOptions.CultureInvariant);
        }

        private static string ExtensionOf(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot <= 0 || dot == name.Length - 1 ? string.Empty : name.Substring(dot);
        }

        /// <summary>True when the pattern can match any file, so it needs no filtering.</summary>
        public static bool IsMatchAll(string? pattern)
        {
            string normalized = Normalize(pattern);
            return normalized == All;
        }

        /// <summary>
        /// Translates the wildcard pattern into a regex body. Used both for matching
        /// in the app and for building the shell's <c>AppliesTo</c> filter.
        /// </summary>
        public static string ToRegex(string pattern)
        {
            string normalized = Normalize(pattern);
            var builder = new StringBuilder(normalized.Length * 2);

            foreach (char c in normalized)
            {
                switch (c)
                {
                    case '*':
                        builder.Append(".*");
                        break;
                    case '!':
                        builder.Append('.');
                        break;
                    default:
                        builder.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Rewrites the pattern into the shell's AQS wildcard syntax, where '?' means
        /// one character and '*' means any run. Returns null for match-all patterns.
        /// </summary>
        public static string? ToShellWildcard(string? pattern)
        {
            if (IsMatchAll(pattern))
            {
                return null;
            }

            string normalized = Normalize(pattern);
            if (normalized.Length == 0)
            {
                return null;
            }

            var builder = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                if (c == '*')
                {
                    builder.Append('*');
                }
                else if (c == '!')
                {
                    builder.Append('?');
                }
                else if (c == '"')
                {
                    // A quote would break the AQS string literal; drop it.
                    continue;
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
