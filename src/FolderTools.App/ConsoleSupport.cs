using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FolderTools.App
{
    /// <summary>
    /// The tool is built as a WinExe so a context-menu launch never flashes a console
    /// window. That also detaches it from the console of a shell that started it, so
    /// <c>FolderTools.exe -i</c> would print nothing; attach back to the parent console
    /// when there is one and let the caller fall back to a dialog otherwise.
    /// </summary>
    internal static class ConsoleSupport
    {
        private const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        private static bool _attached;

        /// <summary>True when output can go to a console the user can see.</summary>
        public static bool TryAttach()
        {
            if (_attached)
            {
                return true;
            }

            if (!AttachConsole(AttachParentProcess))
            {
                return false;
            }

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            _attached = true;
            return true;
        }
    }
}
