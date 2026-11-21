using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace FolderTools.App
{
    /// <summary>
    /// Minimal entry point that deliberately avoids compile-time references to FolderTools.Core.
    /// It can therefore report assembly/type-load failures that happen before Program.Main runs.
    /// </summary>
    internal static class Bootstrap
    {
        private const string ProgramTypeName = "FolderTools.App.Program";
        private const string DependencyDirectoryName = "FolderTools_dlls";
        private static readonly object LogLock = new object();
        private static string? _logPath;
        private static bool _debug;

        [STAThread]
        private static int Main(string[] args)
        {
            string[] suppliedArgs = args ?? Array.Empty<string>();
            _debug = suppliedArgs.Any(IsDebugArgument);
            string[] appArgs = suppliedArgs.Where(argument => !IsDebugArgument(argument)).ToArray();

            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromDependencyDirectory;

            if (_debug)
            {
                WriteDiagnosticHeader(appArgs);
            }

            try
            {
                Assembly application = Assembly.GetExecutingAssembly();
                Type? programType = application.GetType(ProgramTypeName, throwOnError: true);
                MethodInfo? entry = programType?.GetMethod(
                    "Main",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (entry == null)
                {
                    throw new MissingMethodException(ProgramTypeName, "Main(string[])");
                }

                object? result = entry.Invoke(null, new object[] { appArgs });
                int exitCode = result is int code ? code : 0;
                if (_debug)
                {
                    WriteLoadedAssemblies("Application returned exit code " + exitCode + ".");
                }

                return exitCode;
            }
            catch (Exception exception)
            {
                Exception cause = Unwrap(exception);
                WriteFailure(cause);
                string message = "Не удалось запустить FolderTools.";
                if (!string.IsNullOrWhiteSpace(_logPath))
                {
                    message += "\n\nДиагностический журнал: " + _logPath;
                }
                else
                {
                    message += "\n\nНе удалось записать журнал. Подробности:\n" + cause.Message;
                }

                if (_debug)
                {
                    message += "\n\n" + cause;
                }

                MessageBox.Show(message, "FolderTools — ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool IsDebugArgument(string? argument)
        {
            return string.Equals(argument, "/debug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "--debug", StringComparison.OrdinalIgnoreCase);
        }

        private static Assembly? ResolveFromDependencyDirectory(object? sender, ResolveEventArgs args)
        {
            string? simpleName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return null;
            }

            string candidate = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                DependencyDirectoryName,
                simpleName + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        private static Exception Unwrap(Exception exception)
        {
            Exception current = exception;
            while (current is TargetInvocationException invocation && invocation.InnerException != null)
            {
                current = invocation.InnerException;
            }

            return current;
        }

        private static void WriteDiagnosticHeader(IEnumerable<string> appArgs)
        {
            var lines = new List<string>
            {
                "=== FolderTools diagnostic launch ===",
                "UTC: " + DateTime.UtcNow.ToString("O"),
                "Executable: " + Assembly.GetExecutingAssembly().Location,
                "Base directory: " + AppDomain.CurrentDomain.BaseDirectory,
                "Runtime: " + Environment.Version,
                "OS: " + Environment.OSVersion,
                "Debug args removed; forwarded args: " + string.Join(" | ", appArgs),
                "Config exists: " + File.Exists(Assembly.GetExecutingAssembly().Location + ".config"),
                "Dependency directory exists: " + Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DependencyDirectoryName)),
                "Root DLLs: " + ListFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll"),
                "Dependency DLLs: " + ListFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DependencyDirectoryName), "*.dll")
            };
            AppendLog(string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        private static string ListFiles(string directory, string pattern)
        {
            try
            {
                return Directory.Exists(directory)
                    ? string.Join(", ", Directory.GetFiles(directory, pattern).Select(Path.GetFileName))
                    : "<directory missing>";
            }
            catch (Exception exception)
            {
                return "<error: " + exception.Message + ">";
            }
        }

        private static void WriteFailure(Exception exception)
        {
            var builder = new StringBuilder();
            builder.AppendLine("=== FolderTools startup failure ===");
            builder.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
            builder.AppendLine("Executable: " + Assembly.GetExecutingAssembly().Location);
            builder.AppendLine("Base directory: " + AppDomain.CurrentDomain.BaseDirectory);
            builder.AppendLine("Exception:");
            builder.AppendLine(exception.ToString());
            AppendLog(builder.ToString());
            WriteLoadedAssemblies("Assemblies loaded at failure:");
        }

        private static void WriteLoadedAssemblies(string heading)
        {
            try
            {
                var lines = new List<string> { heading };
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(item => item.GetName().Name))
                {
                    string location;
                    try
                    {
                        location = assembly.IsDynamic ? "<dynamic>" : assembly.Location;
                    }
                    catch (Exception exception)
                    {
                        location = "<unavailable: " + exception.Message + ">";
                    }

                    lines.Add("  " + assembly.FullName + " | " + location);
                }

                AppendLog(string.Join(Environment.NewLine, lines) + Environment.NewLine);
            }
            catch (Exception)
            {
                // Logging must never turn a startup diagnostic into another failure.
            }
        }

        private static void AppendLog(string content)
        {
            lock (LogLock)
            {
                foreach (string directory in new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderTools"),
                    Path.GetTempPath()
                })
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        string path = Path.Combine(directory, "startup.log");
                        File.AppendAllText(path, content + Environment.NewLine, new UTF8Encoding(false));
                        _logPath = path;
                        return;
                    }
                    catch (Exception)
                    {
                        // Try the temp directory if LocalAppData is unavailable or unwritable.
                    }
                }
            }
        }
    }
}
