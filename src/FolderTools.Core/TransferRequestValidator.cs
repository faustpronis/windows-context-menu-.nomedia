using System;
using System.Collections.Generic;
using System.Linq;

namespace FolderTools.Core
{
    /// <summary>
    /// Checks a copy/move request before anything is touched on disk.
    ///
    /// The shell is supposed to hide a destination whose patterns do not match the
    /// selected file, but that filter lives in Explorer's hands: a cascading submenu or
    /// a policy setting can surface an item anyway. Re-checking here means a stray click
    /// produces a message instead of a file in the wrong folder.
    /// </summary>
    public static class TransferRequestValidator
    {
        /// <summary>
        /// Returns null when the request is safe to run, otherwise a message for the user.
        /// </summary>
        public static string? Validate(
            Destination? destination,
            IReadOnlyList<string>? items,
            Func<string, bool>? isDirectory = null)
        {
            if (destination == null || string.IsNullOrWhiteSpace(destination.Path))
            {
                return "Папка назначения не найдена в настройках.\n\n" +
                       "Откройте «FolderTools → Настройки…» и проверьте список папок.";
            }

            if (items == null || items.Count == 0)
            {
                return "Не выбраны файлы или папки.";
            }

            Func<string, bool> directoryCheck = isDirectory ?? (_ => false);
            List<string> rejected = items
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Where(item => !destination.MatchesPath(item, directoryCheck(item)))
                .ToList();

            if (rejected.Count == 0)
            {
                return null;
            }

            return "Папка «" + destination.DisplayName + "» не принимает эти объекты: " +
                   string.Join(", ", rejected.Select(PathUtil.GetLeafName)) + ".\n\n" +
                   "Проверьте шаблоны расширений в настройках.";
        }
    }
}
