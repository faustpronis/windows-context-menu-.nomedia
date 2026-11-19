using System;
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
            target = folder ?? string.Empty;
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
