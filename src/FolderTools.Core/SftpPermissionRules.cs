using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FolderTools.Core
{
    /// <summary>Validation helpers for opt-in SFTP chmod rules.</summary>
    public static class SftpPermissionRules
    {
        private static readonly Regex ExtensionPattern = new Regex(@"^\.[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*$", RegexOptions.Compiled);

        /// <summary>
        /// Parses comma-, semicolon-, or newline-separated file extensions. A leading
        /// wildcard is accepted for convenience; stored entries are normalized to ".ext".
        /// </summary>
        public static IReadOnlyList<string> ParseExtensions(string? text)
        {
            var result = new List<string>();
            foreach (string raw in (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = raw.Trim();
                if (value.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(1);
                }
                else if (!value.StartsWith(".", StringComparison.Ordinal))
                {
                    value = "." + value;
                }

                if (!ExtensionPattern.IsMatch(value))
                {
                    throw new FormatException("Недопустимое расширение «" + raw.Trim() + "». Используйте, например, .sh, .py или *.php.");
                }

                if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(value.ToLowerInvariant());
                }
            }

            return result;
        }

        /// <summary>Converts a 3- or 4-digit octal mode to SFTP permission bits.</summary>
        public static bool TryParseMode(string? text, out short mode)
        {
            mode = 0;
            string value = (text ?? string.Empty).Trim();
            if ((value.Length != 3 && value.Length != 4) ||
                (value.Length == 4 && value[0] != '0') ||
                value.Any(c => c < '0' || c > '7'))
            {
                return false;
            }

            try
            {
                int parsed = Convert.ToInt32(value, 8);
                if (parsed < 0 || parsed > Convert.ToInt32("777", 8))
                {
                    return false;
                }

                mode = (short)parsed;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static bool ShouldApply(string? localFilePath, IEnumerable<string>? extensions)
        {
            string fileName = System.IO.Path.GetFileName(localFilePath ?? string.Empty);
            return !string.IsNullOrEmpty(fileName) && (extensions ?? Enumerable.Empty<string>())
                .Any(item => fileName.Length > item.Length && fileName.EndsWith(item, StringComparison.OrdinalIgnoreCase));
        }
    }
}
