using System;
using System.IO;

namespace FolderTools.Core
{
    /// <summary>
    /// Handles the placeholder file that Explorer creates for the "New" menu entry.
    ///
    /// A ShellNew entry cannot produce a file named exactly ".nomedia": the shell
    /// always generates the name itself ("Новый .nomedia.nomedia" and the like) and
    /// then hands that path to the command. So the entry runs
    /// <c>FolderTools.exe shellnew "%1"</c>, which writes the real .nomedia in the
    /// same folder and removes the placeholder.
    /// </summary>
    public static class ShellNewService
    {
        /// <summary>
        /// Creates .nomedia in the folder holding <paramref name="placeholderPath"/> and
        /// deletes the placeholder.
        ///
        /// The deletion is skipped when the placeholder already is .nomedia, which keeps
        /// a shell that generates the exact name from destroying the file just written.
        /// </summary>
        public static NomediaResult CreateFromPlaceholder(
            string placeholderPath,
            DateTime when,
            Action<string>? deleteFile = null)
        {
            if (string.IsNullOrWhiteSpace(placeholderPath))
            {
                throw new ArgumentException("Placeholder path must not be empty.", nameof(placeholderPath));
            }

            string folder = PathUtil.GetDirectoryName(placeholderPath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException(
                    "Placeholder path has no parent folder: " + placeholderPath,
                    nameof(placeholderPath));
            }

            NomediaResult result = NomediaService.Ensure(folder, when);

            if (!string.Equals(
                    PathUtil.GetLeafName(placeholderPath),
                    NomediaService.FileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                (deleteFile ?? DeleteFile)(placeholderPath);
            }

            return result;
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
