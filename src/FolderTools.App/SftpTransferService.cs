using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FolderTools.Core;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace FolderTools.App
{
    internal sealed class SftpTransferResult
    {
        public SftpTransferResult(IReadOnlyList<string> completed, IReadOnlyList<string> errors)
        {
            Completed = completed;
            Errors = errors;
        }

        public IReadOnlyList<string> Completed { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// Password-authenticated SFTP upload. The server host key is pinned after the
    /// user explicitly accepts its SHA-256 fingerprint on first connection.
    /// </summary>
    internal sealed class SftpTransferService
    {
        public SftpTransferResult Transfer(
            Destination destination,
            TransferAction action,
            IEnumerable<string> items,
            string username,
            string password,
            Func<string, bool> trustUnknownHost)
        {
            Validate(destination, username, password, trustUnknownHost);
            var completed = new List<string>();
            var errors = new List<string>();

            using (SftpClient client = Connect(destination, username, password, trustUnknownHost))
            {
                string targetRoot = NormalizeRemotePath(destination.SftpRemotePath ?? string.Empty);
                if (!client.Exists(targetRoot) || !client.GetAttributes(targetRoot).IsDirectory)
                {
                    throw new DirectoryNotFoundException("Удалённая папка не найдена: " + targetRoot);
                }

                foreach (string source in (items ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)))
                {
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
                            UploadDirectory(client, source, target);
                            if (action == TransferAction.Move)
                            {
                                Directory.Delete(source, true);
                            }
                        }
                        else
                        {
                            UploadFileAtomically(client, source, target);

                            if (action == TransferAction.Move)
                            {
                                File.Delete(source);
                            }
                        }

                        completed.Add(source + " → " + target);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(source + ": " + ex.Message);
                        if (action == TransferAction.Move)
                        {
                            break;
                        }
                    }
                }
            }

            return new SftpTransferResult(completed, errors);
        }

        internal static void TestConnection(Destination destination, string username, string password, Func<string, bool> trustUnknownHost)
        {
            Validate(destination, username, password, trustUnknownHost);
            using (SftpClient client = Connect(destination, username, password, trustUnknownHost))
            {
                string remotePath = NormalizeRemotePath(destination.SftpRemotePath ?? string.Empty);
                if (!client.Exists(remotePath) || !client.GetAttributes(remotePath).IsDirectory)
                {
                    throw new DirectoryNotFoundException("Удалённая папка не найдена: " + remotePath);
                }

                client.ListDirectory(remotePath).Take(1).ToList();
            }
        }

        private static SftpClient Connect(Destination destination, string username, string password, Func<string, bool> trustUnknownHost)
        {
            var client = new SftpClient(destination.SftpHost!.Trim(), destination.SftpPort, username, password)
            {
                OperationTimeout = TimeSpan.FromSeconds(60),
                KeepAliveInterval = TimeSpan.FromSeconds(15)
            };

            string? pinnedFingerprint = NormalizeFingerprint(destination.SftpHostKeyFingerprint);
            client.HostKeyReceived += (sender, args) =>
            {
                string actual = NormalizeFingerprint(args.FingerPrintSHA256) ?? string.Empty;
                if (!string.IsNullOrEmpty(pinnedFingerprint))
                {
                    args.CanTrust = string.Equals(pinnedFingerprint, actual, StringComparison.Ordinal);
                    return;
                }

                args.CanTrust = !string.IsNullOrEmpty(actual) && trustUnknownHost(actual);
                if (args.CanTrust)
                {
                    destination.SftpHostKeyFingerprint = actual;
                }
            };

            try
            {
                client.Connect();
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static void Validate(Destination destination, string username, string password, Func<string, bool> trustUnknownHost)
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

        private static void UploadDirectory(SftpClient client, string localDirectory, string remoteDirectory)
        {
            client.CreateDirectory(remoteDirectory);
            try
            {
                foreach (string file in Directory.GetFiles(localDirectory))
                {
                    string remoteFile = JoinRemote(remoteDirectory, Path.GetFileName(file));
                    UploadFileAtomically(client, file, remoteFile);
                }

                foreach (string subdirectory in Directory.GetDirectories(localDirectory))
                {
                    UploadDirectory(client, subdirectory, JoinRemote(remoteDirectory, Path.GetFileName(subdirectory)));
                }
            }
            catch
            {
                try
                {
                    DeleteRemoteDirectory(client, remoteDirectory);
                }
                catch
                {
                    // Preserve the original transfer error; local sources remain untouched.
                }

                throw;
            }
        }

        private static void UploadFileAtomically(SftpClient client, string localFile, string remoteFile)
        {
            string temporary = remoteFile + ".foldertools-" + Guid.NewGuid().ToString("N") + ".uploading";
            try
            {
                using (var input = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    client.UploadFile(input, temporary, false);
                }

                long actualLength = client.GetAttributes(temporary).Size;
                long expectedLength = new FileInfo(localFile).Length;
                if (actualLength != expectedLength)
                {
                    throw new IOException("Размер загруженного файла не совпадает с исходным.");
                }

                client.RenameFile(temporary, remoteFile);
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
    }
}
