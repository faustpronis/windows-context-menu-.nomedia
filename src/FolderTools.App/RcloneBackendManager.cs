using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FolderTools.App
{
    internal enum RemoteEngine
    {
        Rclone,
        Bclone
    }

    internal sealed class RemoteProviderInfo
    {
        public RemoteProviderInfo(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }
        public override string ToString() => string.IsNullOrWhiteSpace(Description) ? Name : Name + " — " + Description;
    }

    internal sealed class RemoteTestResult
    {
        public RemoteTestResult(string remote, bool success, string message)
        {
            Remote = remote;
            Success = success;
            Message = message;
        }

        public string Remote { get; }
        public bool Success { get; }
        public string Message { get; }
    }

    /// <summary>Manages the optional external rclone/bclone executable and its private config.</summary>
    internal static class RcloneBackendManager
    {
        // `config providers` may exceed JavaScriptSerializer's default 2 MiB limit,
        // especially in forks with additional backends. Keep parsing bounded while
        // allowing the complete provider catalogue to be captured and deserialized.
        private const int MaxCaptureBytes = 32 * 1024 * 1024;

        public static string RuntimeDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote-tools");
        public static string ConfigDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote-config");

        public static string ExecutablePath(RemoteEngine engine)
        {
            return Path.Combine(RuntimeDirectory, engine == RemoteEngine.Rclone ? "rclone.exe" : "bclone.exe");
        }

        public static string ConfigPath(RemoteEngine engine)
        {
            return Path.Combine(ConfigDirectory, engine == RemoteEngine.Rclone ? "rclone.conf" : "bclone.conf");
        }

        public static string GetVersion(RemoteEngine engine)
        {
            string exe = ExecutablePath(engine);
            if (!File.Exists(exe))
            {
                return "не установлен";
            }

            ProcessResult result = Run(engine, new[] { "version" }, null, false);
            if (result.ExitCode != 0)
            {
                return "ошибка запуска: " + FirstLine(result.Error);
            }
            return FirstLine(result.Output);
        }

        public static void InstallOrUpdate(RemoteEngine engine)
        {
            string repo = engine == RemoteEngine.Rclone ? "rclone/rclone" : "BenjiThatFoxGuy/bclone";
            string exeName = engine == RemoteEngine.Rclone ? "rclone.exe" : "bclone.exe";
            string assetPrefix = engine == RemoteEngine.Rclone ? "rclone-" : "bclone-";
            string apiUrl = "https://api.github.com/repos/" + repo + "/releases/latest";
            string downloadUrl;
            string expectedDigest;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "FolderTools";
                string json = client.DownloadString(apiUrl);
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaxCaptureBytes };
                var release = serializer.DeserializeObject(json) as IDictionary<string, object>;
                var assets = release != null && release.TryGetValue("assets", out object? assetsValue)
                    ? assetsValue as object[]
                    : null;
                IDictionary<string, object>? asset = assets?
                    .OfType<IDictionary<string, object>>()
                    .FirstOrDefault(item =>
                        item.TryGetValue("name", out object? name) &&
                        Convert.ToString(name, System.Globalization.CultureInfo.InvariantCulture) != null &&
                        Convert.ToString(name, System.Globalization.CultureInfo.InvariantCulture)!.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase) &&
                        Convert.ToString(name, System.Globalization.CultureInfo.InvariantCulture)!.IndexOf("windows-amd64.zip", StringComparison.OrdinalIgnoreCase) >= 0);
                if (asset == null || !asset.TryGetValue("browser_download_url", out object? urlValue))
                {
                    throw new InvalidOperationException("В последнем GitHub-релизе " + repo + " не найден Windows x64 ZIP-архив.");
                }
                downloadUrl = Convert.ToString(urlValue, System.Globalization.CultureInfo.InvariantCulture) ??
                              throw new InvalidOperationException("GitHub не вернул ссылку на архив.");
                if (!asset.TryGetValue("digest", out object? digestValue) ||
                    !(Convert.ToString(digestValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                    .StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("GitHub Releases API не предоставил SHA-256 для архива. Загрузка отменена.");
                }
                expectedDigest = Convert.ToString(digestValue, System.Globalization.CultureInfo.InvariantCulture)!.Substring("sha256:".Length);
            }

            Directory.CreateDirectory(RuntimeDirectory);
            string tempZip = Path.Combine(RuntimeDirectory, Guid.NewGuid().ToString("N") + ".zip");
            string tempExe = Path.Combine(RuntimeDirectory, exeName + ".new");
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "FolderTools";
                    client.DownloadFile(downloadUrl, tempZip);
                }

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                using (var archiveStream = File.OpenRead(tempZip))
                {
                    string actualDigest = BitConverter.ToString(sha256.ComputeHash(archiveStream)).Replace("-", string.Empty);
                    if (!string.Equals(actualDigest, expectedDigest, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("SHA-256 загруженного архива не совпадает с GitHub Releases API. Архив не установлен.");
                    }
                }

                bool extracted = false;
                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(item =>
                        string.Equals(Path.GetFileName(item.FullName), exeName, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        throw new InvalidDataException("В загруженном архиве не найден " + exeName + ".");
                    }
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                        output.Flush(true);
                    }
                    extracted = true;
                }

                if (!extracted || new FileInfo(tempExe).Length < 1024 * 1024)
                {
                    throw new InvalidDataException("Загруженный исполняемый файл неожиданно мал или повреждён.");
                }

                string finalExe = ExecutablePath(engine);
                if (File.Exists(finalExe))
                {
                    string backup = finalExe + ".old";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Replace(tempExe, finalExe, backup, true);
                    try { File.Delete(backup); } catch (IOException) { }
                }
                else
                {
                    File.Move(tempExe, finalExe);
                }

                string version = GetVersion(engine);
                if (version.StartsWith("ошибка", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Файл установлен, но проверка версии не прошла: " + version);
                }
            }
            finally
            {
                TryDelete(tempZip);
                TryDelete(tempExe);
            }
        }

        public static IReadOnlyList<RemoteProviderInfo> GetProviders(RemoteEngine engine, string? configPassword = null)
        {
            ProcessResult result = Run(engine, new[] { "config", "providers" }, configPassword, true);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(FirstLine(result.Error.Length == 0 ? result.Output : result.Error));
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = MaxCaptureBytes };
            var root = serializer.DeserializeObject(result.Output) as IDictionary<string, object>;
            if (root == null || !root.TryGetValue("Providers", out object? value) || !(value is object[] providers))
            {
                throw new InvalidDataException("Backend-ы не распознаны: rclone/bclone вернул неизвестный JSON-формат.");
            }

            var parsed = new List<RemoteProviderInfo>();
            foreach (object item in providers)
            {
                if (!(item is IDictionary<string, object> provider) || !provider.TryGetValue("Name", out object? nameValue))
                {
                    continue;
                }
                string name = Convert.ToString(nameValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                if (name.Length == 0) continue;
                string description = provider.TryGetValue("Description", out object? descriptionValue)
                    ? Convert.ToString(descriptionValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                parsed.Add(new RemoteProviderInfo(name, description));
            }
            return parsed.OrderBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static IReadOnlyList<RemoteTestResult> TestConfiguredRemotes(RemoteEngine engine, string? configPassword = null)
        {
            ProcessResult list = Run(engine, new[] { "listremotes" }, configPassword, true);
            if (list.ExitCode != 0)
            {
                throw new InvalidOperationException(FirstLine(list.Error.Length == 0 ? list.Output : list.Error));
            }

            string[] remotes = list.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(remote => remote.Trim().TrimEnd(':'))
                .Where(remote => remote.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (remotes.Length == 0)
            {
                return new[] { new RemoteTestResult("(нет настроенных подключений)", false,
                    "В конфигурации пока нет remote-подключений.") };
            }

            return remotes.Select(remote =>
            {
                ProcessResult test = Run(engine, new[] { "lsd", remote + ":", "--max-depth", "1" }, configPassword, true);
                return new RemoteTestResult(remote, test.ExitCode == 0,
                    test.ExitCode == 0 ? "Подключение и просмотр каталогов прошли." : FirstLine(test.Error.Length == 0 ? test.Output : test.Error));
            }).ToArray();
        }

        private static ProcessResult Run(RemoteEngine engine, IEnumerable<string> arguments, string? configPassword, bool useConfig)
        {
            string executable = ExecutablePath(engine);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("Не установлен выбранный движок: " + executable, executable);
            }

            Directory.CreateDirectory(ConfigDirectory);
            var args = new List<string> { "--config", Quote(ConfigPath(engine)) };
            args.AddRange(arguments.Select(Quote));
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = RuntimeDirectory
            };
            if (!string.IsNullOrEmpty(configPassword))
            {
                start.EnvironmentVariables["RCLONE_CONFIG_PASS"] = configPassword;
            }

            using (Process process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить " + executable))
            {
                Task<string> outputTask = ReadBoundedAsync(process.StandardOutput);
                Task<string> errorTask = ReadBoundedAsync(process.StandardError);
                if (!process.WaitForExit(120000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    try { process.WaitForExit(5000); } catch (InvalidOperationException) { }
                    throw new TimeoutException("Команда " + Path.GetFileName(executable) + " не завершилась за 120 секунд.");
                }
                Task.WaitAll(outputTask, errorTask);
                string output = outputTask.Result;
                string error = errorTask.Result;
                return new ProcessResult(process.ExitCode, output, error);
            }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            var output = new StringBuilder(Math.Min(MaxCaptureBytes, 8192));
            char[] buffer = new char[8192];
            int captured = 0;
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                int keep = Math.Min(count, MaxCaptureBytes - captured);
                if (keep > 0)
                {
                    output.Append(buffer, 0, keep);
                    captured += keep;
                }
            }
            if (captured >= MaxCaptureBytes) output.Append("\r\n[вывод ограничен]");
            return output.ToString();
        }

        private static string Quote(string value)
        {
            string input = value ?? string.Empty;
            var output = new StringBuilder(input.Length + 2);
            output.Append('"');
            int slashes = 0;
            foreach (char character in input)
            {
                if (character == '\\')
                {
                    slashes++;
                    continue;
                }

                if (character == '"')
                {
                    output.Append('\\', slashes * 2 + 1);
                    output.Append('"');
                    slashes = 0;
                    continue;
                }

                output.Append('\\', slashes);
                output.Append(character);
                slashes = 0;
            }
            output.Append('\\', slashes * 2);
            output.Append('"');
            return output.ToString();
        }

        private static string FirstLine(string value)
        {
            string line = (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "неизвестная ошибка";
            return line.Length > 500 ? line.Substring(0, 500) : line;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }

        private sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }
            public int ExitCode { get; }
            public string Output { get; }
            public string Error { get; }
        }
    }
}
