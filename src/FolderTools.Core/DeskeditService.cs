using System;
using System.Diagnostics;
using System.IO;

namespace FolderTools.Core
{
    /// <summary>Launches an external application with the given arguments.</summary>
    public interface IProcessLauncher
    {
        void Launch(string fileName, string arguments);
    }

    public sealed class ProcessLauncher : IProcessLauncher
    {
        public void Launch(string fileName, string arguments)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
    }

    public enum DeskeditOutcome
    {
        /// <summary>The configured editor was started.</summary>
        Launched,

        /// <summary>No editor path is configured or the file is missing.</summary>
        EditorMissing,

        /// <summary>The editor exists but could not be started.</summary>
        LaunchFailed
    }

    /// <summary>Starts the desktop.ini editor (DeskEdit) for a folder.</summary>
    public sealed class DeskeditService
    {
        private readonly IProcessLauncher _launcher;
        private readonly Func<string, bool> _fileExists;

        public DeskeditService(IProcessLauncher launcher, Func<string, bool>? fileExists = null)
        {
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _fileExists = fileExists ?? File.Exists;
        }

        /// <summary>Runs the editor with the folder pre-selected.</summary>
        public DeskeditOutcome OpenFolder(string? editorPath, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(editorPath) || !_fileExists(editorPath!))
            {
                return DeskeditOutcome.EditorMissing;
            }

            try
            {
                _launcher.Launch(editorPath!, "\"" + folderPath + "\"");
                return DeskeditOutcome.Launched;
            }
            catch (Exception)
            {
                return DeskeditOutcome.LaunchFailed;
            }
        }
    }
}
