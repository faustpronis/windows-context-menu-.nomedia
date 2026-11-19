using System;
using System.IO;

namespace FolderTools.Core
{
    /// <summary>
    /// Copies an icon chosen anywhere on disk into the installation's icons folder.
    ///
    /// The settings file then refers to it relatively, so the installation can be
    /// moved or zipped up without the icons breaking, and a user deleting the source
    /// file does not blank out the menu.
    /// </summary>
    public static class IconStore
    {
        public const string DefaultExtension = ".ico";

        /// <summary>
        /// Copies <paramref name="sourcePath"/> into the icons folder and returns the
        /// stored (relative) path.
        ///
        /// The name is derived from the destination id, so re-picking an icon replaces
        /// the old file instead of leaving orphans behind. When a file with that name
        /// already holds the same bytes, it is left untouched.
        /// </summary>
        public static string Import(string baseDirectory, string destinationId, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("Base directory must not be empty.", nameof(baseDirectory));
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("Icon path must not be empty.", nameof(sourcePath));
            }

            if (string.IsNullOrWhiteSpace(destinationId))
            {
                throw new ArgumentException("Destination id must not be empty.", nameof(destinationId));
            }

            string source = sourcePath!.Trim().Trim('"');
            if (!File.Exists(PathUtil.ToNative(source)))
            {
                throw new FileNotFoundException("Файл иконки не найден: " + source, source);
            }

            string extension = Path.GetExtension(source);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = DefaultExtension;
            }

            string iconsDirectory = AppPaths.IconsDirectory(baseDirectory);
            if (!Directory.Exists(iconsDirectory))
            {
                Directory.CreateDirectory(iconsDirectory);
            }

            string fileName = SanitizeId(destinationId) + extension.ToLowerInvariant();
            string target = Path.Combine(iconsDirectory, fileName);
            string sourceNative = PathUtil.ToNative(source);

            // Copying onto the same file would truncate it, which is easy to hit when
            // the user re-picks the icon that is already in place.
            if (!IsSameFile(sourceNative, target))
            {
                byte[] bytes = File.ReadAllBytes(sourceNative);
                if (!File.Exists(target) || !SameContent(target, bytes))
                {
                    File.WriteAllBytes(target, bytes);
                }
            }

            return AppPaths.ToStored(baseDirectory, target);
        }

        /// <summary>
        /// Removes a stored icon file. Returns false when the path points outside the
        /// icons folder, so a hand-edited settings file cannot delete arbitrary files.
        /// </summary>
        public static bool Delete(string baseDirectory, string? storedPath)
        {
            if (!AppPaths.IsInside(baseDirectory, storedPath))
            {
                return false;
            }

            string full = AppPaths.Resolve(baseDirectory, storedPath);
            string iconsDirectory = Path.GetFullPath(AppPaths.IconsDirectory(baseDirectory))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!Path.GetFullPath(full).StartsWith(iconsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(full))
            {
                return false;
            }

            File.Delete(full);
            return true;
        }

        private static bool IsSameFile(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left),
                    Path.GetFullPath(right),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SameContent(string path, byte[] bytes)
        {
            byte[] existing = File.ReadAllBytes(path);
            if (existing.Length != bytes.Length)
            {
                return false;
            }

            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != bytes[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Keeps the generated file name to characters every file system accepts.</summary>
        private static string SanitizeId(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                         || (c >= '0' && c <= '9') || c == '-' || c == '_';
                builder.Append(safe ? c : '_');
            }

            string result = builder.ToString();
            return result.Length > 0 ? result : "icon";
        }
    }
}
