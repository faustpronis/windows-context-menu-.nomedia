using System;
using System.IO;

namespace FolderTools.Core
{
    /// <summary>
    /// Locations the tool owns, all relative to the folder holding the executable.
    ///
    /// Keeping everything beside the exe means the whole installation is one folder
    /// that can be copied or moved as a unit, and the settings file never contains an
    /// absolute path that would break after such a move.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Subfolder that holds the icons copied out of the file system.</summary>
        public const string IconsFolderName = "icons";

        public static string SettingsPath(string baseDirectory)
        {
            return SettingsStore.DefaultPath(baseDirectory);
        }

        public static string IconsDirectory(string baseDirectory)
        {
            return Path.Combine(baseDirectory, IconsFolderName);
        }

        /// <summary>
        /// Turns a stored path into a usable one. Relative values are resolved against
        /// <paramref name="baseDirectory"/>, absolute values are returned unchanged, so a
        /// hand-written absolute path still works.
        /// </summary>
        public static string Resolve(string baseDirectory, string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
            {
                return string.Empty;
            }

            string value = storedPath!.Trim().Trim('"');
            if (IsRooted(value))
            {
                return value;
            }

            return Path.Combine(baseDirectory, PathUtil.ToNative(value));
        }

        /// <summary>
        /// The form stored in the settings file: a path inside the installation becomes
        /// relative, anything else is kept as written.
        /// </summary>
        public static string ToStored(string baseDirectory, string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            string full = Path.GetFullPath(fullPath!.Trim().Trim('"'));
            string baseFull = Path.GetFullPath(baseDirectory);

            string prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;

            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(prefix.Length);
            }

            return full;
        }

        /// <summary>
        /// True for a path that already names a full location.
        ///
        /// Path.IsPathRooted only understands the running platform, but the settings
        /// file holds Windows paths that are also read by tests on Linux, so a drive
        /// letter or a UNC prefix is recognised explicitly.
        /// </summary>
        public static bool IsRooted(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string value = path!.Trim().Trim('"');
            if (value.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            bool driveLetter = value.Length >= 2
                && char.IsLetter(value[0])
                && value[1] == ':';

            return driveLetter || Path.IsPathRooted(value);
        }

        /// <summary>True when a stored path points inside the installation folder.</summary>
        public static bool IsInside(string baseDirectory, string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
            {
                return false;
            }

            string value = storedPath!.Trim().Trim('"');
            return !IsRooted(value);
        }
    }
}
