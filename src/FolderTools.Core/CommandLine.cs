using System;
using System.Collections.Generic;

namespace FolderTools.Core
{
    public enum CommandKind
    {
        Nomedia,
        Deskedit,
        Install,
        Uninstall,
        SetDeskEdit,
        Version,
        Help,
        Unknown
    }

    /// <summary>Result of parsing the command line: a command plus its target folder.</summary>
    public sealed class ParsedCommand
    {
        public ParsedCommand(CommandKind kind, string? folder, string? value)
        {
            Kind = kind;
            Folder = folder;
            Value = value;
        }

        public CommandKind Kind { get; }

        /// <summary>Target folder for Nomedia / Deskedit commands.</summary>
        public string? Folder { get; }

        /// <summary>Extra payload, e.g. the editor path for SetDeskEdit.</summary>
        public string? Value { get; }
    }

    /// <summary>
    /// Parses the tool's command line. Verbs are case-insensitive and may be
    /// followed by a folder path in quotes.
    /// </summary>
    public static class CommandLine
    {
        public static ParsedCommand Parse(IReadOnlyList<string> args)
        {
            if (args == null || args.Count == 0)
            {
                return new ParsedCommand(CommandKind.Help, null, null);
            }

            string verb = (args[0] ?? string.Empty).Trim();
            string? first = args.Count > 1 ? args[1] : null;

            switch (verb.ToLowerInvariant())
            {
                case "nomedia":
                    return new ParsedCommand(CommandKind.Nomedia, first, null);
                case "deskedit":
                    return new ParsedCommand(CommandKind.Deskedit, first, null);
                case "install":
                case "-i":
                case "/i":
                case "--install":
                    return new ParsedCommand(CommandKind.Install, null, null);
                case "uninstall":
                case "remove":
                case "-r":
                case "/r":
                case "-u":
                case "/u":
                case "-remove":
                case "--remove":
                case "--uninstall":
                    return new ParsedCommand(CommandKind.Uninstall, null, null);
                case "set-deskedit":
                    return new ParsedCommand(CommandKind.SetDeskEdit, null, first);
                case "-v":
                case "/v":
                case "--version":
                case "version":
                    return new ParsedCommand(CommandKind.Version, null, null);
                case "/?":
                case "-?":
                case "/h":
                case "-h":
                case "--help":
                case "help":
                    return new ParsedCommand(CommandKind.Help, null, null);
                default:
                    // A bare path is treated as a request to create .nomedia there.
                    if (verb.Length > 0 && (verb.Contains("\\") || verb.Contains(":")))
                    {
                        return new ParsedCommand(CommandKind.Nomedia, verb, null);
                    }

                    return new ParsedCommand(CommandKind.Unknown, null, verb);
            }
        }

        public static string HelpText =>
            "FolderTools — инструменты для текущей папки.\n" +
            "\n" +
            "Использование:\n" +
            "  FolderTools.exe -i, --install           Добавить пункт в контекстное меню папки\n" +
            "  FolderTools.exe -r, -remove, --uninstall Удалить пункт из контекстного меню\n" +
            "  FolderTools.exe nomedia \"<папка>\"       Создать или обновить .nomedia\n" +
            "  FolderTools.exe deskedit \"<папка>\"       Открыть desktop.ini в DeskEdit\n" +
            "  FolderTools.exe set-deskedit \"<путь>\"    Запомнить путь к DeskEdit.exe\n" +
            "  FolderTools.exe -v, --version            Показать версию\n";
    }
}
