using System;
using System.Globalization;

namespace FolderTools.Core
{
    public enum SftpConflictPolicy
    {
        ReplaceAll,
        SkipExisting,
        PreserveExistingWithConflictRename
    }

    /// <summary>Per-operation metadata override; unset fields preserve the configured/server behavior.</summary>
    public sealed class SftpTransferOptions
    {
        public SftpConflictPolicy ConflictPolicy { get; set; } = SftpConflictPolicy.SkipExisting;
        public bool ApplyExplicitPermissions { get; set; }
        public string FileMode { get; set; } = "0644";
        public string DirectoryMode { get; set; } = "0755";
        public int? UserId { get; set; }
        public int? GroupId { get; set; }
    }

    public static class SftpConflictNaming
    {
        public static string CreateName(string name, DateTime timestamp, int sequence = 1)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Не задано имя объекта.", nameof(name));
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            int dot = name.LastIndexOf('.');
            string stem = dot > 0 ? name.Substring(0, dot) : name;
            string extension = dot > 0 ? name.Substring(dot) : string.Empty;
            string suffix = sequence == 1 ? string.Empty : "-" + sequence.ToString(CultureInfo.InvariantCulture);
            return stem + "-conflict-" + timestamp.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + suffix + extension;
        }
    }
}
