using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace FolderTools.App
{
    /// <summary>Ensures that one settings window is open per interactive logon session.</summary>
    internal static class SettingsWindowCoordinator
    {
        private const string MutexName = @"Local\FolderTools.SettingsWindow";
        public const string WindowTitle = "FolderTools — настройки";

        public static int Run(Func<int> showSettings)
        {
            if (showSettings == null)
            {
                throw new ArgumentNullException(nameof(showSettings));
            }

            using (var mutex = new Mutex(true, MutexName, out bool createdNew))
            {
                if (!createdNew)
                {
                    if (!ActivateExistingWindow())
                    {
                        System.Windows.Forms.MessageBox.Show(
                            "Окно настроек уже запускается, но пока не отвечает. Подождите немного и попробуйте снова.",
                            "FolderTools",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Information);
                    }
                    return 0;
                }

                try
                {
                    return showSettings();
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static bool ActivateExistingWindow()
        {
            var timer = Stopwatch.StartNew();
            IntPtr window = IntPtr.Zero;
            while (window == IntPtr.Zero && timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                window = FindWindow(null, WindowTitle);
                if (window == IntPtr.Zero)
                {
                    Thread.Sleep(100);
                }
            }

            if (window != IntPtr.Zero)
            {
                ShowWindow(window, 9); // SW_RESTORE also brings a minimized form back.
                SetForegroundWindow(window);
                return true;
            }

            return false;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW", SetLastError = true)]
        private static extern IntPtr FindWindow(string? className, string windowName);

        [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr window);
    }
}
