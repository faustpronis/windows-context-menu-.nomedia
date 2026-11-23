using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FolderTools.Core;

namespace FolderTools.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ParsedCommand command = CommandLine.Parse(args);
            try
            {
                return Run(command);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return 2;
            }
        }

        private static int Run(ParsedCommand command)
        {
            switch (command.Kind)
            {
                case CommandKind.Nomedia:
                    return RunNomedia(command.Folder);
                case CommandKind.ShellNew:
                    return RunShellNew(command.Folder);
                case CommandKind.Deskedit:
                    return RunDeskedit(command.Folder);
                case CommandKind.Install:
                    ContextMenuInstaller.Install();
                    Report(
                        "FolderTools " + ProductInfo.Version + " зарегистрирован в контекстном меню папок.",
                        ProductInfo.ExecutablePath + "\n" +
                        "Правый клик по папке (или по пустому месту внутри неё) → FolderTools.");
                    return 0;
                case CommandKind.Uninstall:
                    ContextMenuInstaller.Uninstall();
                    Report(
                        "FolderTools удалён из контекстного меню папок.",
                        "Если меню осталось открытым, закройте и откройте его снова.");
                    return 0;
                case CommandKind.Settings:
                    return RunSettings();
                case CommandKind.CopyTo:
                    return RunTransfer(TransferAction.Copy, command);
                case CommandKind.MoveTo:
                    return RunTransfer(TransferAction.Move, command);
                case CommandKind.SetDeskEdit:
                    ContextMenuInstaller.SetDeskEditPath(command.Value);
                    Report("Путь к DeskEdit сохранён.", command.Value);
                    return 0;
                case CommandKind.Version:
                    Report(ProductInfo.VersionText, ProductInfo.ExecutablePath);
                    return 0;
                case CommandKind.Help:
                    ShowInfo(CommandLine.HelpText);
                    return 0;
                default:
                    ShowError("Неизвестная команда. " + CommandLine.HelpText);
                    return 2;
            }
        }

        /// <summary>
        /// Writes to the calling console when there is one, otherwise shows a dialog:
        /// the same command is used both from a shell and from Explorer.
        /// </summary>
        private static void Report(string message, string? detail = null)
        {
            string text = string.IsNullOrWhiteSpace(detail) ? message : message + "\n" + detail;
            if (ConsoleSupport.TryAttach())
            {
                Console.WriteLine(text);
                return;
            }

            ShowInfo(text);
        }

        private static int RunSettings()
        {
            using (var form = new SettingsForm())
            {
                form.ShowDialog();
            }

            return 0;
        }

        /// <summary>
        /// Routes one selected item to the folder behind the chosen destination.
        ///
        /// The shell runs the verb once per selected item, so each invocation carries a
        /// single path. Nothing is shown on success: the file simply appears in the
        /// target folder, which is the whole point of the menu item.
        /// </summary>
        private static int RunTransfer(TransferAction action, ParsedCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Folder))
            {
                ShowError("Не указан получатель.");
                return 2;
            }

            if (command.Items.Count == 0)
            {
                ShowError("Не выбраны файлы или папки.");
                return 2;
            }

            FolderToolsSettings settings = ContextMenuInstaller.LoadSettings();
            Destination? destination = settings.Destinations
                .FirstOrDefault(d => string.Equals(d.Id, command.Folder, StringComparison.OrdinalIgnoreCase));

            string? rejection = TransferRequestValidator.Validate(
                destination,
                command.Items,
                path => Directory.Exists(path));

            if (rejection != null)
            {
                ShowError(rejection);
                return 3;
            }

            if (destination.Type == DestinationType.Sftp)
            {
                return RunSftpTransfer(action, destination, settings, command.Items);
            }

            var service = new TransferService();
            TransferResult result = service.Transfer(action, destination.Path, command.Items);

            if (result.HasErrors)
            {
                string verb = action == TransferAction.Copy ? "скопировать" : "переместить";
                ShowError(
                    "Не удалось " + verb + " часть объектов в «" + destination.DisplayName + "»:\n\n" +
                    string.Join("\n", result.Errors));
                return 4;
            }

            return 0;
        }

        private static int RunSftpTransfer(
            TransferAction action,
            Destination destination,
            FolderToolsSettings settings,
            System.Collections.Generic.IReadOnlyList<string> items)
        {
            SftpCredentialLookup credentials;
            try
            {
                credentials = SftpCredentialStore.Get(destination.Id);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось прочитать защищённые учётные данные SFTP:\n" + ex.Message);
                return 5;
            }

            if (credentials.State != SftpCredentialState.Available)
            {
                string reason = credentials.State == SftpCredentialState.NeedsReentry
                    ? "Пароль SFTP не удалось расшифровать после смены компьютера или учётной записи Windows."
                    : "Для этой учётной записи Windows пароль SFTP ещё не сохранён.";
                ShowError(reason + "\n\nОткройте FolderTools → Настройки…, выберите SFTP-папку и введите пароль заново.");
                return 5;
            }

            try
            {
                var service = new SftpTransferService();
                SftpTransferProgressForm dialog = null!;
                using (dialog = new SftpTransferProgressForm((progress, cancellationToken) => service.Transfer(
                    destination,
                    action,
                    items,
                    credentials.Username ?? string.Empty,
                    credentials.Password ?? string.Empty,
                    (endpoint, fingerprint) => dialog.InvokeOnUi(() =>
                        ConfirmAndRememberSftpHostKey(endpoint, fingerprint, destination, settings, dialog)),
                    progress,
                    cancellationToken)))
                {
                    dialog.ShowDialog();
                }

                if (dialog.Failure != null)
                {
                    ShowError("Ошибка SFTP-подключения к «" + destination.DisplayName + "»:\n\n" + dialog.Failure.Message);
                    return 4;
                }

                SftpTransferResult result = dialog.Result ?? new SftpTransferResult(
                    Array.Empty<string>(), new[] { "Передача не вернула результат." }, false, 0);
                ShowSftpTransferResult(result, destination, action);
                return result.HasErrors ? 4 : (result.Cancelled ? 6 : 0);
            }
            catch (Exception ex)
            {
                ShowError("Ошибка SFTP-подключения к «" + destination.DisplayName + "»:\n\n" + ex.Message);
                return 4;
            }
        }

        private static bool ConfirmAndRememberSftpHostKey(
            SftpEndpoint endpoint,
            string fingerprint,
            Destination destination,
            FolderToolsSettings settings,
            IWin32Window? owner = null)
        {
            DialogResult answer = MessageBox.Show(
                owner,
                "У этого адреса ещё не сохранён SSH-ключ. Сверьте fingerprint с администратором сервера.\n\n" +
                "Сервер: " + endpoint.Host + ":" + endpoint.Port + "\n" +
                "SHA-256: " + fingerprint + "\n\nДоверять этому ключу и сохранить его только для этого адреса?",
                "Проверка SSH-ключа сервера",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                return false;
            }

            if (endpoint.IsPrimary)
            {
                destination.SftpHostKeyFingerprint = fingerprint;
            }
            else
            {
                SftpEndpoint? backup = destination.SftpFallbacks.FirstOrDefault(e =>
                    e.Port == endpoint.Port && string.Equals(e.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase));
                if (backup != null)
                {
                    backup.HostKeyFingerprint = fingerprint;
                }
            }
            ContextMenuInstaller.SaveSettings(settings);
            return true;
        }

        private static void ShowSftpTransferResult(SftpTransferResult result, Destination destination, TransferAction action)
        {
            string state = result.Cancelled ? "отменена" : result.HasErrors ? "завершена с ошибками" : "завершена успешно";
            string title = action == TransferAction.Copy ? "SFTP-копирование" : "SFTP-перемещение";
            string text = "Передача в «" + destination.DisplayName + "» " + state + ".\n\n" +
                          "Успешно: " + result.Completed.Count + "\n" +
                          "Ошибок: " + result.Errors.Count + "\n" +
                          "Передано: " + FormatBytes(result.BytesTransferred);
            if (result.Errors.Count > 0)
            {
                text += "\n\n" + string.Join("\n", result.Errors.Take(8));
                if (result.Errors.Count > 8)
                {
                    text += "\n… и ещё " + (result.Errors.Count - 8) + " ошибок.";
                }
            }

            MessageBox.Show(text, title, MessageBoxButtons.OK,
                result.HasErrors || result.Cancelled ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " Б";
            }
            double value = bytes;
            string[] units = { "КБ", "МБ", "ГБ", "ТБ" };
            int unit = -1;
            do
            {
                value /= 1024.0;
                unit++;
            } while (value >= 1024.0 && unit < units.Length - 1);
            return value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " " + units[unit];
        }

        private static int RunNomedia(string? folder)
        {
            if (!TryResolveFolder(folder, out string target))
            {
                return 2;
            }

            NomediaResult result = NomediaService.Ensure(target, DateTime.Now);
            if (!AppConfig.ShowSuccess)
            {
                return 0;
            }

            switch (result.Outcome)
            {
                case NomediaOutcome.Created:
                    ShowInfo(".nomedia создан в папке:\n" + target + "\n\n" + result.Block);
                    break;
                case NomediaOutcome.Appended:
                    ShowInfo(".nomedia обновлён: добавлена запись для этой папки.\n\n" + result.Block);
                    break;
                case NomediaOutcome.AlreadyPresent:
                    ShowInfo("Запись для этой папки уже есть в .nomedia.\nИзменений не требуется.");
                    break;
            }

            return 0;
        }

        /// <summary>
        /// Turns the placeholder Explorer creates for the New menu entry into a real
        /// .nomedia. Nothing is reported on success: the entry is meant to behave like
        /// the built-in "New folder" command.
        /// </summary>
        private static int RunShellNew(string? placeholder)
        {
            if (string.IsNullOrWhiteSpace(placeholder))
            {
                ShowError("Не передан файл, созданный проводником.");
                return 2;
            }

            ShellNewService.CreateFromPlaceholder(placeholder!, DateTime.Now);
            return 0;
        }

        private static int RunDeskedit(string? folder)
        {
            if (!TryResolveFolder(folder, out string target))
            {
                return 2;
            }

            string? editor = AppConfig.ResolveDeskEdit();
            var service = new DeskeditService(new ProcessLauncher());
            DeskeditOutcome outcome = service.OpenFolder(editor, target);

            switch (outcome)
            {
                case DeskeditOutcome.Launched:
                    return 0;
                case DeskeditOutcome.EditorMissing:
                    ShowError(
                        "Не найден DeskEdit.exe (Desktop.ini Editor).\n\n" +
                        "Скачайте его и распакуйте, например, в C:\\Tools\\DeskEdit:\n" +
                        AppConfig.DeskEditDownloadPage + "\n\n" +
                        "Затем укажите путь командой:\n" +
                        "  FolderTools.exe set-deskedit \"C:\\Tools\\DeskEdit\\DeskEdit_x64.exe\"");
                    return 3;
                default:
                    ShowError("Не удалось запустить DeskEdit.exe.");
                    return 3;
            }
        }

        private static bool TryResolveFolder(string? folder, out string target)
        {
            target = PathUtil.NormalizeDriveRoot(folder);
            if (string.IsNullOrWhiteSpace(target))
            {
                ShowError("Не указана папка.\n\n" + CommandLine.HelpText);
                return false;
            }

            if (!System.IO.Directory.Exists(target))
            {
                ShowError("Папка не найдена:\n" + target);
                return false;
            }

            return true;
        }

        private static void ShowInfo(string message)
        {
            MessageBox.Show(message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, "FolderTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
