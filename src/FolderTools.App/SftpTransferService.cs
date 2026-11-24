using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FolderTools.Core;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace FolderTools.App
{
    internal sealed class SftpTransferProgress
    {
        public string Status { get; set; } = string.Empty;
        public string CurrentFile { get; set; } = string.Empty;
        public long FileBytesTransferred { get; set; }
        public long FileBytesTotal { get; set; }
        public long BytesTransferredTotal { get; set; }
        public long BytesTotal { get; set; }
        public int CompletedItems { get; set; }
        public int TotalItems { get; set; }
    }

    internal sealed class SftpTransferResult
    {
        public SftpTransferResult(IReadOnlyList<string> completed, IReadOnlyList<string> errors, bool cancelled, long bytesTransferred)
        {
            Completed = completed;
            Errors = errors;
            Cancelled = cancelled;
            BytesTransferred = bytesTransferred;
        }

        public IReadOnlyList<string> Completed { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Cancelled { get; }
        public long BytesTransferred { get; }
        public bool HasErrors => Errors.Count > 0;
    }

    internal sealed class SftpConnectionTestReport
    {
        public bool ConnectionSucceeded { get; set; }
        public bool DirectoryExists { get; set; }
        public bool DirectoryReadable { get; set; }
        public bool DirectoryWritable { get; set; }
        public bool DirectoryMissing { get; set; }
        public string ConnectionMessage { get; set; } = string.Empty;
        public string DirectoryMessage { get; set; } = string.Empty;
        public string ReadMessage { get; set; } = string.Empty;
        public string WriteMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// SFTP upload with endpoint failover. Candidate endpoints connect concurrently;
    /// the first authenticated endpoint with access to the configured directory wins.
    /// Unknown host keys are never accepted implicitly and are confirmed after the
    /// initial race, one endpoint at a time.
    /// </summary>
    internal sealed class SftpTransferService
    {
        public SftpTransferResult Transfer(
            Destination destination,
            TransferAction action,
            IEnumerable<string> items,
            string username,
            string password,
            Func<SftpEndpoint, string, bool> trustUnknownHost,
            Action<SftpTransferProgress>? reportProgress = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(destination, username, password, trustUnknownHost);
            if ((destination.SftpChmodExtensions ?? new List<string>()).Count > 0 &&
                !SftpPermissionRules.TryParseMode(destination.SftpChmodMode, out _))
            {
                throw new ArgumentException("Некорректный chmod режим в настройках SFTP.");
            }

            var completed = new List<string>();
            var errors = new List<string>();
            List<string> sources = (items ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            var files = new List<string>();
            foreach (string source in sources)
            {
                if (File.Exists(source))
                {
                    files.Add(source);
                }
                else if (Directory.Exists(source))
                {
                    files.AddRange(Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories));
                }
            }

            long totalBytes = 0;
            foreach (string file in files)
            {
                totalBytes = checked(totalBytes + new FileInfo(file).Length);
            }

            var tracker = new ProgressTracker(reportProgress, totalBytes, sources.Count);
            tracker.Report("Проверка SFTP-адресов…", string.Empty, 0, 0);
            long uploadedBytes = 0;
            bool cancelled = false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (SftpClient client = ConnectFastest(destination, username, password, trustUnknownHost, tracker, cancellationToken))
                {
                    string targetRoot = NormalizeRemotePath(destination.SftpRemotePath ?? string.Empty);
                    foreach (string source in sources)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (!File.Exists(source) && !Directory.Exists(source))
                            {
                                throw new FileNotFoundException("Локальный объект не найден.", source);
                            }

                            string leaf = PathUtil.GetLeafName(source);
                            bool isDirectory = Directory.Exists(source);
                            string target = FindAvailablePath(client, targetRoot, leaf, isDirectory);
                            if (isDirectory)
                            {
                                UploadDirectory(client, source, target, destination, tracker, cancellationToken);
                                if (action == TransferAction.Move)
                                {
                                    Directory.Delete(source, true);
                                }
                            }
                            else
                            {
                                UploadFileAtomically(client, source, target, destination, tracker, cancellationToken);
                                if (action == TransferAction.Move)
                                {
                                    File.Delete(source);
                                }
                            }

                            completed.Add(source + " → " + target);
                            tracker.CompleteItem(source);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            errors.Add(source + ": " + ex.Message);
                            tracker.FailItem(source);
                            if (action == TransferAction.Move)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            uploadedBytes = tracker.BytesTransferred;
            tracker.Report(cancelled ? "Передача отменена" : (errors.Count == 0 ? "Передача завершена" : "Передача завершена с ошибками"), string.Empty, 0, 0);
            return new SftpTransferResult(completed, errors, cancelled, uploadedBytes);
        }

        internal static void TestConnection(
            Destination destination,
            string username,
            string password,
            Func<SftpEndpoint, string, bool> trustUnknownHost)
        {
            SftpConnectionTestReport report = TestConnectionDetailed(destination, username, password, trustUnknownHost);
            if (!report.ConnectionSucceeded || !report.DirectoryExists || !report.DirectoryReadable || !report.DirectoryWritable)
            {
                throw new IOException(string.Join("\n", new[]
                {
                    report.ConnectionMessage,
                    report.DirectoryMessage,
                    report.ReadMessage,
                    report.WriteMessage
                }.Where(message => !string.IsNullOrWhiteSpace(message))));
            }
        }

        internal static SftpConnectionTestReport TestConnectionDetailed(
            Destination destination,
            string username,
            string password,
            Func<SftpEndpoint, string, bool> trustUnknownHost)
        {
            var report = new SftpConnectionTestReport();
            try
            {
                Validate(destination, username, password, trustUnknownHost);
                Destination connection = destination.Clone();
                connection.SftpRemotePath = ".";
                using (SftpClient client = ConnectFastest(connection, username, password, trustUnknownHost, null, CancellationToken.None))
                {
                    CopyHostKeys(connection, destination);
                    report.ConnectionSucceeded = true;
                    report.ConnectionMessage = "Сервер доступен, SSH-аутентификация прошла.";

                    string remotePath = NormalizeRemotePath(destination.SftpRemotePath ?? string.Empty);
                    try
                    {
                        if (!client.Exists(remotePath))
                        {
                            report.DirectoryMissing = true;
                            report.DirectoryMessage = "Удалённая папка не существует: " + remotePath;
                            report.ReadMessage = "Чтение не проверено: папка отсутствует.";
                            report.WriteMessage = "Запись не проверена: папка отсутствует.";
                            return report;
                        }
                        if (!client.GetAttributes(remotePath).IsDirectory)
                        {
                            report.DirectoryMessage = "Путь существует, но это не папка: " + remotePath;
                            report.ReadMessage = "Чтение не проверено: путь не является папкой.";
                            report.WriteMessage = "Запись не проверена: путь не является папкой.";
                            return report;
                        }

                        report.DirectoryExists = true;
                        report.DirectoryMessage = "Папка существует.";
                    }
                    catch (Exception ex)
                    {
                        report.DirectoryMessage = "Не удалось проверить папку: " + HumanizeSftpError(ex);
                        report.ReadMessage = "Чтение не проверено.";
                        report.WriteMessage = "Запись не проверена.";
                        return report;
                    }

                    try
                    {
                        client.ListDirectory(remotePath).Take(1).ToList();
                        report.DirectoryReadable = true;
                        report.ReadMessage = "Содержимое папки доступно для чтения.";
                    }
                    catch (Exception ex)
                    {
                        report.ReadMessage = "Не удалось прочитать содержимое папки: " + HumanizeSftpError(ex);
                    }

                    string probe = JoinRemote(remotePath, ".foldertools-write-test-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        using (Stream output = client.Create(probe))
                        {
                            output.WriteByte(0x46);
                            output.Flush();
                        }
                        report.DirectoryWritable = true;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            if (client.Exists(probe))
                            {
                                client.DeleteFile(probe);
                            }
                        }
                        catch
                        {
                            report.WriteMessage = "Не удалось удалить временный тестовый файл «" + probe + "». ";
                        }
                        report.WriteMessage += "Запись не подтверждена: " + HumanizeSftpError(ex);
                    }

                    if (report.DirectoryWritable)
                    {
                        try
                        {
                            client.DeleteFile(probe);
                            report.WriteMessage = "Запись и удаление временного тестового файла работают.";
                        }
                        catch (Exception ex)
                        {
                            report.WriteMessage = "Запись разрешена, но тестовый файл не удалось удалить: " + probe +
                                                  ". Причина: " + HumanizeSftpError(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                report.ConnectionMessage = HumanizeConnectionError(ex);
            }

            return report;
        }

        internal static void CreateRemoteDirectory(
            Destination destination,
            string username,
            string password,
            Func<SftpEndpoint, string, bool> trustUnknownHost)
        {
            try
            {
                Validate(destination, username, password, trustUnknownHost);
                Destination connection = destination.Clone();
                connection.SftpRemotePath = ".";
                using (SftpClient client = ConnectFastest(connection, username, password, trustUnknownHost, null, CancellationToken.None))
                {
                    CopyHostKeys(connection, destination);
                    string path = NormalizeRemotePath(destination.SftpRemotePath ?? string.Empty);
                    if (path.Length == 0 || path == ".")
                    {
                        throw new ArgumentException("Не задан путь удалённой папки для создания.");
                    }
                    if (!path.StartsWith("/", StringComparison.Ordinal))
                    {
                        path = JoinRemote(client.WorkingDirectory, path);
                    }

                    string current = "/";
                    foreach (string segment in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        current = current == "/" ? "/" + segment : current + "/" + segment;
                        if (client.Exists(current))
                        {
                            if (!client.GetAttributes(current).IsDirectory)
                            {
                                throw new IOException("Часть пути существует, но не является папкой: " + current);
                            }
                        }
                        else
                        {
                            client.CreateDirectory(current);
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is ArgumentException) && !(ex is IOException && ex.Message.StartsWith("Часть пути", StringComparison.Ordinal)))
            {
                throw new IOException("Не удалось создать удалённую папку: " + HumanizeSftpError(ex), ex);
            }
        }

        private static void CopyHostKeys(Destination source, Destination target)
        {
            target.SftpHostKeyFingerprint = source.SftpHostKeyFingerprint;
            target.SftpFallbacks = source.SftpFallbacks;
        }

        private static string HumanizeConnectionError(Exception exception)
        {
            string type = exception.GetType().Name;
            string message = HumanizeSftpError(exception);
            string fullMessage = exception.ToString();
            if (type.IndexOf("Authentication", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullMessage.IndexOf("authentication failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullMessage.IndexOf("permission denied (password)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SFTP-сервер отвечает, но не принял логин/пароль: " + message;
            }
            if (type.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Не удалось установить соединение с SFTP-сервером. Проверьте адрес, порт и сеть: " + message;
            }
            return "Не удалось подключиться к SFTP-серверу: " + message;
        }

        private static string HumanizeSftpError(Exception exception)
        {
            string message = exception.Message ?? string.Empty;
            if (message.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                exception.GetType().Name.IndexOf("PermissionDenied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                exception is UnauthorizedAccessException)
            {
                return "у пользователя нет необходимых прав доступа (Permission denied).";
            }
            if (message.IndexOf("no such file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                exception is DirectoryNotFoundException ||
                exception.GetType().Name.IndexOf("PathNotFound", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "указанный путь не найден на сервере.";
            }
            if (message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                exception.GetType().Name.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "истекло время ожидания ответа сервера.";
            }
            return message.Length == 0 ? exception.GetType().Name : message;
        }

        internal static SftpDirectoryBrowserSession OpenDirectoryBrowser(
            Destination destination,
            string username,
            string password,
            Func<SftpEndpoint, string, bool> trustUnknownHost)
        {
            Destination connection = destination.Clone();
            connection.SftpRemotePath = ".";
            Validate(connection, username, password, trustUnknownHost);
            SftpClient client = ConnectFastest(connection, username, password, trustUnknownHost, null, CancellationToken.None);
            destination.SftpHostKeyFingerprint = connection.SftpHostKeyFingerprint;
            destination.SftpFallbacks = connection.SftpFallbacks;
            return new SftpDirectoryBrowserSession(client);
        }

        private static SftpClient ConnectFastest(
            Destination destination,
            string username,
            string password,
            Func<SftpEndpoint, string, bool> trustUnknownHost,
            ProgressTracker? progress,
            CancellationToken cancellationToken)
        {
            List<SftpEndpoint> endpoints = GetEndpoints(destination);
            var rejectedUnknownKeys = new HashSet<SftpEndpoint>();
            var failures = new List<string>();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unknownFingerprints = new ConcurrentDictionary<SftpEndpoint, string>();
                using (var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    List<Task<SftpClient>> tasks = endpoints
                        .Where(endpoint => !rejectedUnknownKeys.Contains(endpoint))
                        .Select(endpoint => ConnectCandidateAsync(
                            destination, endpoint, username, password, unknownFingerprints,
                            raceCancellation.Token, progress))
                        .ToList();

                    while (tasks.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Task<SftpClient> finished = Task.WhenAny(tasks).GetAwaiter().GetResult();
                        tasks.Remove(finished);
                        try
                        {
                            SftpClient winner = finished.GetAwaiter().GetResult();
                            raceCancellation.Cancel();
                            foreach (Task<SftpClient> remaining in tasks)
                            {
                                DisposeWhenComplete(remaining);
                            }
                            return winner;
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // Another candidate won the race or its connection was cancelled.
                        }
                        catch (Exception ex)
                        {
                            failures.Add(ex.Message);
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                bool savedAnyKey = false;
                foreach (SftpEndpoint endpoint in endpoints)
                {
                    if (!unknownFingerprints.TryGetValue(endpoint, out string? fingerprint) || rejectedUnknownKeys.Contains(endpoint))
                    {
                        continue;
                    }

                    if (trustUnknownHost(endpoint, fingerprint))
                    {
                        SaveFingerprint(destination, endpoint, fingerprint);
                        savedAnyKey = true;
                    }
                    else
                    {
                        rejectedUnknownKeys.Add(endpoint);
                        failures.Add(endpoint.Host + ": SSH-ключ не подтверждён пользователем.");
                    }
                }

                if (!savedAnyKey)
                {
                    string detail = failures.Count == 0 ? "Нет доступных адресов." : string.Join("\n", failures.Distinct());
                    throw new IOException("Не удалось подключиться ни к одному SFTP-адресу:\n" + detail);
                }
            }
        }

        private static async Task<SftpClient> ConnectCandidateAsync(
            Destination destination,
            SftpEndpoint endpoint,
            string username,
            string password,
            ConcurrentDictionary<SftpEndpoint, string> unknownFingerprints,
            CancellationToken cancellationToken,
            ProgressTracker? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка " + endpoint.Host + ":" + endpoint.Port + "…", string.Empty, 0, 0);
            var client = new SftpClient(endpoint.Host.Trim(), endpoint.Port, username, password)
            {
                OperationTimeout = TimeSpan.FromSeconds(60),
                KeepAliveInterval = TimeSpan.FromSeconds(15)
            };
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
            string? pinnedFingerprint = NormalizeFingerprint(endpoint.HostKeyFingerprint);
            client.HostKeyReceived += (sender, args) =>
            {
                string actual = NormalizeFingerprint(args.FingerPrintSHA256) ?? string.Empty;
                if (string.IsNullOrEmpty(pinnedFingerprint))
                {
                    if (!string.IsNullOrEmpty(actual))
                    {
                        unknownFingerprints[endpoint] = actual;
                    }
                    args.CanTrust = false;
                    return;
                }

                args.CanTrust = string.Equals(pinnedFingerprint, actual, StringComparison.Ordinal);
            };

            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                string remotePath = NormalizeRemotePath(destination.SftpRemotePath ?? string.Empty);
                if (remotePath == ".")
                {
                    remotePath = client.WorkingDirectory;
                }
                if (!client.Exists(remotePath) || !client.GetAttributes(remotePath).IsDirectory)
                {
                    throw new DirectoryNotFoundException("Удалённая папка не найдена на " + endpoint.Host + ": " + remotePath);
                }
                client.ListDirectory(remotePath).Take(1).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static void DisposeWhenComplete(Task<SftpClient> task)
        {
            task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    t.Result.Dispose();
                }
                else if (t.IsFaulted)
                {
                    var ignored = t.Exception;
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static List<SftpEndpoint> GetEndpoints(Destination destination)
        {
            var endpoints = new List<SftpEndpoint>
            {
                new SftpEndpoint
                {
                    Host = destination.SftpHost!.Trim(),
                    Port = destination.SftpPort,
                    HostKeyFingerprint = destination.SftpHostKeyFingerprint,
                    IsPrimary = true
                }
            };
            foreach (SftpEndpoint backup in destination.SftpFallbacks ?? new List<SftpEndpoint>())
            {
                if (endpoints.Count >= 8)
                {
                    break;
                }
                if (!string.IsNullOrWhiteSpace(backup.Host) && backup.Port >= 1 && backup.Port <= 65535 &&
                    !endpoints.Any(existing => existing.Port == backup.Port && string.Equals(existing.Host, backup.Host, StringComparison.OrdinalIgnoreCase)))
                {
                    endpoints.Add(backup.Clone());
                }
            }
            return endpoints;
        }

        private static void SaveFingerprint(Destination destination, SftpEndpoint endpoint, string fingerprint)
        {
            endpoint.HostKeyFingerprint = fingerprint;
            if (endpoint.IsPrimary)
            {
                destination.SftpHostKeyFingerprint = fingerprint;
                return;
            }

            SftpEndpoint? backup = (destination.SftpFallbacks ?? new List<SftpEndpoint>())
                .FirstOrDefault(e => e.Port == endpoint.Port && string.Equals(e.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase));
            if (backup != null)
            {
                backup.HostKeyFingerprint = fingerprint;
            }
        }

        private static void Validate(
            Destination destination,
            string username,
            string password,
            Func<SftpEndpoint, string, bool> trustUnknownHost)
        {
            if (destination == null || destination.Type != DestinationType.Sftp)
            {
                throw new ArgumentException("Получатель не настроен как SFTP.", nameof(destination));
            }
            if (string.IsNullOrWhiteSpace(destination.SftpHost))
            {
                throw new ArgumentException("Не указан адрес SFTP-сервера.");
            }
            if (destination.SftpPort < 1 || destination.SftpPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Порт SFTP должен быть от 1 до 65535.");
            }
            if (string.IsNullOrWhiteSpace(destination.SftpRemotePath))
            {
                throw new ArgumentException("Не указана удалённая папка SFTP.");
            }
            if ((destination.SftpFallbacks ?? new List<SftpEndpoint>()).Count > 7)
            {
                throw new ArgumentException("Можно настроить не более семи резервных SFTP-адресов.");
            }
            if (string.IsNullOrWhiteSpace(username) || password == null)
            {
                throw new ArgumentException("Для этого SFTP-получателя нужно заново указать имя пользователя и пароль.");
            }
            if (trustUnknownHost == null)
            {
                throw new ArgumentNullException(nameof(trustUnknownHost));
            }
        }

        private static string? NormalizeFingerprint(string? fingerprint)
        {
            return string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint!.Trim();
        }

        private static string FindAvailablePath(SftpClient client, string parent, string name, bool directory)
        {
            string candidate = JoinRemote(parent, name);
            if (!client.Exists(candidate))
            {
                return candidate;
            }
            string extension = string.Empty;
            string stem = name;
            if (!directory)
            {
                int dot = name.LastIndexOf('.');
                if (dot > 0 && dot < name.Length - 1)
                {
                    stem = name.Substring(0, dot);
                    extension = name.Substring(dot);
                }
            }
            for (int i = 2; i < 10000; i++)
            {
                candidate = JoinRemote(parent, stem + " (" + i + ")" + extension);
                if (!client.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new IOException("Не удалось подобрать свободное имя для «" + name + "».");
        }

        private static void UploadDirectory(
            SftpClient client,
            string localDirectory,
            string remoteDirectory,
            Destination destination,
            ProgressTracker tracker,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string temporaryDirectory = remoteDirectory + ".foldertools-" + Guid.NewGuid().ToString("N") + ".uploading";
            client.CreateDirectory(temporaryDirectory);
            try
            {
                foreach (string file in Directory.GetFiles(localDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string remoteFile = JoinRemote(temporaryDirectory, Path.GetFileName(file));
                    UploadFileAtomically(client, file, remoteFile, destination, tracker, cancellationToken);
                }
                foreach (string subdirectory in Directory.GetDirectories(localDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    UploadDirectory(client, subdirectory, JoinRemote(temporaryDirectory, Path.GetFileName(subdirectory)), destination, tracker, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                client.RenameFile(temporaryDirectory, remoteDirectory);
            }
            catch
            {
                try
                {
                    DeleteRemoteDirectory(client, temporaryDirectory);
                }
                catch
                {
                    // Preserve the original error; local sources remain untouched.
                }
                throw;
            }
        }

        private static void UploadFileAtomically(
            SftpClient client,
            string localFile,
            string remoteFile,
            Destination destination,
            ProgressTracker tracker,
            CancellationToken cancellationToken)
        {
            string temporary = remoteFile + ".foldertools-" + Guid.NewGuid().ToString("N") + ".uploading";
            long expectedLength = new FileInfo(localFile).Length;
            tracker.BeginFile(localFile, expectedLength);
            try
            {
                using (var input = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    client.UploadFile(input, temporary, false, uploaded =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long current = uploaded > (ulong)expectedLength ? expectedLength : (long)uploaded;
                        tracker.UpdateFile(current);
                    });
                }

                cancellationToken.ThrowIfCancellationRequested();
                long actualLength = client.GetAttributes(temporary).Size;
                if (actualLength != expectedLength)
                {
                    throw new IOException("Размер загруженного файла не совпадает с исходным.");
                }

                if (SftpPermissionRules.ShouldApply(localFile, destination.SftpChmodExtensions))
                {
                    if (!SftpPermissionRules.TryParseMode(destination.SftpChmodMode, out short mode))
                    {
                        throw new IOException("Некорректный chmod режим в настройках SFTP.");
                    }
                    client.ChangePermissions(temporary, mode);
                }

                cancellationToken.ThrowIfCancellationRequested();
                client.RenameFile(temporary, remoteFile);
                tracker.CompleteFile(localFile, expectedLength);
            }
            catch
            {
                try
                {
                    if (client.Exists(temporary))
                    {
                        client.DeleteFile(temporary);
                    }
                }
                catch
                {
                    // Preserve the transfer error. A leftover temporary file is not a completed target.
                }
                throw;
            }
        }

        private static void DeleteRemoteDirectory(SftpClient client, string remoteDirectory)
        {
            foreach (ISftpFile entry in client.ListDirectory(remoteDirectory))
            {
                if (entry.Name == "." || entry.Name == "..")
                {
                    continue;
                }
                if (entry.IsDirectory)
                {
                    DeleteRemoteDirectory(client, entry.FullName);
                }
                else
                {
                    client.DeleteFile(entry.FullName);
                }
            }
            client.DeleteDirectory(remoteDirectory);
        }

        private static string NormalizeRemotePath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }
            return normalized;
        }

        private static string JoinRemote(string parent, string child)
        {
            return NormalizeRemotePath(parent) + "/" + child.Trim('/');
        }

        private sealed class ProgressTracker
        {
            private readonly Action<SftpTransferProgress>? _report;
            private readonly long _totalBytes;
            private readonly int _totalItems;
            private long _bytesTransferred;
            private long _currentBytes;
            private long _currentFileLength;
            private int _completedItems;
            private string _currentFile = string.Empty;

            public ProgressTracker(Action<SftpTransferProgress>? report, long totalBytes, int totalItems)
            {
                _report = report;
                _totalBytes = totalBytes;
                _totalItems = totalItems;
            }

            public long BytesTransferred => _bytesTransferred;

            public void BeginFile(string file, long length)
            {
                _currentFile = file;
                _currentFileLength = length;
                _currentBytes = 0;
                Report("Загрузка файла…", file, 0, length);
            }

            public void UpdateFile(long bytes)
            {
                long current = Math.Max(0, Math.Min(bytes, _currentFileLength));
                if (current > _currentBytes)
                {
                    _bytesTransferred += current - _currentBytes;
                }
                _currentBytes = current;
                Report("Загрузка файла…", _currentFile, _currentBytes, _currentFileLength);
            }

            public void CompleteFile(string file, long length)
            {
                if (length > _currentBytes)
                {
                    _bytesTransferred += length - _currentBytes;
                }
                _currentBytes = length;
                _currentFileLength = 0;
                Report("Файл загружен", file, length, length);
                _currentBytes = 0;
            }

            public void CompleteItem(string source)
            {
                _completedItems++;
                Report("Объект отправлен", source, 0, 0);
            }

            public void FailItem(string source)
            {
                _currentBytes = 0;
                _currentFileLength = 0;
                Report("Ошибка объекта", source, 0, 0);
            }

            public void Report(string status, string file, long fileBytes, long fileTotal)
            {
                if (_report == null)
                {
                    return;
                }
                try
                {
                    _report(new SftpTransferProgress
                    {
                        Status = status,
                        CurrentFile = file,
                        FileBytesTransferred = fileBytes,
                        FileBytesTotal = fileTotal,
                        BytesTransferredTotal = BytesTransferred,
                        BytesTotal = _totalBytes,
                        CompletedItems = _completedItems,
                        TotalItems = _totalItems
                    });
                }
                catch
                {
                    // Progress UI closing must not interrupt or corrupt an SFTP transfer.
                }
            }
        }
    }

    internal sealed class SftpRemoteDirectory
    {
        public SftpRemoteDirectory(string name, string fullName)
        {
            Name = name;
            FullName = fullName;
        }

        public string Name { get; }
        public string FullName { get; }

        public override string ToString() => Name;
    }

    internal sealed class SftpDirectoryBrowserSession : IDisposable
    {
        private readonly SftpClient _client;

        internal SftpDirectoryBrowserSession(SftpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public string HomeDirectory => _client.WorkingDirectory;

        public IReadOnlyList<SftpRemoteDirectory> ListDirectories(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            if (normalized.Length == 0)
            {
                normalized = _client.WorkingDirectory;
            }
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            if (!_client.Exists(normalized) || !_client.GetAttributes(normalized).IsDirectory)
            {
                throw new DirectoryNotFoundException("Удалённая папка не найдена: " + normalized);
            }

            return _client.ListDirectory(normalized)
                .Where(entry => entry.IsDirectory && entry.Name != "." && entry.Name != "..")
                .Select(entry => new SftpRemoteDirectory(entry.Name, entry.FullName))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
