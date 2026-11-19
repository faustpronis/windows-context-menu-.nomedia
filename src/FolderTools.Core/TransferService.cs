using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FolderTools.Core
{
    /// <summary>One item that was copied or moved, and where it ended up.</summary>
    public sealed class TransferItemResult
    {
        public TransferItemResult(string source, string target, bool renamed)
        {
            Source = source;
            Target = target;
            Renamed = renamed;
        }

        public string Source { get; }

        public string Target { get; }

        /// <summary>True when the target name had to be changed to avoid overwriting.</summary>
        public bool Renamed { get; }
    }

    /// <summary>Result of one transfer batch.</summary>
    public sealed class TransferResult
    {
        public TransferResult(
            TransferAction action,
            string destinationFolder,
            IReadOnlyList<TransferItemResult> items,
            IReadOnlyList<string> errors)
        {
            Action = action;
            DestinationFolder = destinationFolder;
            Items = items;
            Errors = errors;
        }

        public TransferAction Action { get; }

        public string DestinationFolder { get; }

        public IReadOnlyList<TransferItemResult> Items { get; }

        public IReadOnlyList<string> Errors { get; }

        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// Copies or moves selected files and folders into one destination folder.
    ///
    /// Files are never overwritten: a name clash produces "name (2).ext". Moves are
    /// performed item by item and stop at the first failure, so a batch cannot leave
    /// half of a selection gone without a clear error.
    /// </summary>
    public sealed class TransferService
    {
        private readonly Func<string, bool> _fileExists;
        private readonly Func<string, bool> _directoryExists;
        private readonly Action<string, string> _copyFile;
        private readonly Action<string, string> _moveFile;
        private readonly Action<string, string> _moveDirectory;
        private readonly Action<string, string> _copyDirectory;

        /// <summary>Production wiring: real file system operations.</summary>
        public TransferService()
            : this(
                path => File.Exists(PathUtil.ToNative(path)),
                path => Directory.Exists(PathUtil.ToNative(path)),
                (from, to) => File.Copy(PathUtil.ToNative(from), PathUtil.ToNative(to), false),
                (from, to) => File.Move(PathUtil.ToNative(from), PathUtil.ToNative(to)),
                (from, to) => Directory.Move(PathUtil.ToNative(from), PathUtil.ToNative(to)),
                (from, to) => CopyDirectoryRecursive(PathUtil.ToNative(from), PathUtil.ToNative(to)))
        {
        }

        /// <summary>Injectable wiring, so the routing rules can be tested without disk IO.</summary>
        public TransferService(
            Func<string, bool> fileExists,
            Func<string, bool> directoryExists,
            Action<string, string> copyFile,
            Action<string, string> moveFile,
            Action<string, string> moveDirectory,
            Action<string, string> copyDirectory)
        {
            _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
            _directoryExists = directoryExists ?? throw new ArgumentNullException(nameof(directoryExists));
            _copyFile = copyFile ?? throw new ArgumentNullException(nameof(copyFile));
            _moveFile = moveFile ?? throw new ArgumentNullException(nameof(moveFile));
            _moveDirectory = moveDirectory ?? throw new ArgumentNullException(nameof(moveDirectory));
            _copyDirectory = copyDirectory ?? throw new ArgumentNullException(nameof(copyDirectory));
        }

        /// <summary>
        /// Transfers <paramref name="items"/> into <paramref name="destinationFolder"/>.
        /// Items already inside that folder are skipped rather than copied onto themselves.
        /// </summary>
        public TransferResult Transfer(
            TransferAction action,
            string destinationFolder,
            IEnumerable<string> items)
        {
            if (string.IsNullOrWhiteSpace(destinationFolder))
            {
                throw new ArgumentException("Destination folder must not be empty.", nameof(destinationFolder));
            }

            var done = new List<TransferItemResult>();
            var errors = new List<string>();
            List<string> sources = (items ?? Enumerable.Empty<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            if (!_directoryExists(destinationFolder))
            {
                try
                {
                    Directory.CreateDirectory(PathUtil.ToNative(destinationFolder));
                }
                catch (Exception ex)
                {
                    errors.Add("Не удалось создать папку «" + destinationFolder + "»: " + ex.Message);
                    return new TransferResult(action, destinationFolder, done, errors);
                }
            }

            foreach (string source in sources)
            {
                bool isDirectory = _directoryExists(source);
                bool isFile = !isDirectory && _fileExists(source);
                if (!isDirectory && !isFile)
                {
                    errors.Add("Не найден объект: " + source);
                    continue;
                }

                if (isDirectory && PathUtil.AreSameFolder(source, destinationFolder))
                {
                    errors.Add("Пропущено (уже в целевой папке): " + source);
                    continue;
                }

                if (isFile && PathUtil.AreSameFolder(PathUtil.GetDirectoryName(source), destinationFolder))
                {
                    errors.Add("Пропущено (уже в целевой папке): " + source);
                    continue;
                }

                string name = PathUtil.GetLeafName(source);

                string target = BuildAvailablePath(destinationFolder, name, isDirectory);
                bool renamed = !string.Equals(
                    PathUtil.Combine(destinationFolder, name),
                    target,
                    StringComparison.OrdinalIgnoreCase);

                try
                {
                    if (isDirectory)
                    {
                        if (action == TransferAction.Move)
                        {
                            _moveDirectory(source, target);
                        }
                        else
                        {
                            _copyDirectory(source, target);
                        }
                    }
                    else if (action == TransferAction.Move)
                    {
                        _moveFile(source, target);
                    }
                    else
                    {
                        _copyFile(source, target);
                    }

                    done.Add(new TransferItemResult(source, target, renamed));
                }
                catch (Exception ex)
                {
                    errors.Add("Ошибка при обработке «" + source + "»: " + ex.Message);

                    // A move that fails midway must not silently continue: the user
                    // needs to know which items were already transferred.
                    if (action == TransferAction.Move)
                    {
                        break;
                    }
                }
            }

            return new TransferResult(action, destinationFolder, done, errors);
        }

        /// <summary>
        /// Finds a free name in the destination, adding " (2)", " (3)" and so on.
        /// Existing files are never replaced.
        /// </summary>
        public string BuildAvailablePath(string folder, string name, bool isDirectory)
        {
            string candidate = PathUtil.Combine(folder, name);
            if (!Exists(candidate, isDirectory))
            {
                return candidate;
            }

            string stem;
            string extension;
            if (isDirectory)
            {
                stem = name;
                extension = string.Empty;
            }
            else
            {
                extension = GetExtension(name);
                stem = name.Substring(0, name.Length - extension.Length);
            }

            for (int i = 2; i < 10000; i++)
            {
                string numbered = stem + " (" + i + ")" + extension;
                candidate = PathUtil.Combine(folder, numbered);
                if (!Exists(candidate, isDirectory))
                {
                    return candidate;
                }
            }

            throw new IOException("Не удалось подобрать свободное имя для «" + name + "».");
        }

        private bool Exists(string path, bool isDirectory)
        {
            return isDirectory ? _directoryExists(path) || _fileExists(path) : _fileExists(path) || _directoryExists(path);
        }

        /// <summary>
        /// Extension including the dot, or an empty string. Mirrors the resolver's rule
        /// so a name like ".gitignore" is treated as having no extension.
        /// </summary>
        private static string GetExtension(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot <= 0 || dot == name.Length - 1 ? string.Empty : name.Substring(dot);
        }

        /// <summary>
        /// Recursive copy for real IO. Both arguments already use the native separator,
        /// so Path.Combine is correct here and keeps nested paths consistent with the
        /// platform instead of reintroducing a backslash on non-Windows hosts.
        /// </summary>
        private static void CopyDirectoryRecursive(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, PathUtil.GetLeafName(file)), false);
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                CopyDirectoryRecursive(directory, Path.Combine(target, PathUtil.GetLeafName(directory)));
            }
        }
    }
}
