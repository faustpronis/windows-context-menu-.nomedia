# AGENTS.md

## Репозиторий

- GitHub: `faustpronis/windows-context-menu-.nomedia` (public). Работа идёт в `main`,
  и `main` — ветка по умолчанию. Однажды дефолтной осталась ветка с работой после
  merge PR, из-за чего главная страница показывала старый README: при слиянии PR
  стоит проверять `default_branch` в API, а не только факт merge.
- CI: `.github/workflows/build.yml` на `windows-latest` — build + test + артефакт
  `FolderTools-<версия>.zip`, релиз на тег `v*`.
- Бинарники живут только в GitHub Releases (`FolderTools-<версия>.zip`); в репозитории
  их нет — устаревшая копия в git уже удалена. Версия задаётся тегом `vX.Y.Z` и
  подставляется в сборку через `-p:Version=`, шаг «Verify version stamp» падает,
  если версия не попала в exe.
- Проверить меню в проводнике в этой среде нельзя (нет Windows). Всё, что можно
  покрыть тестами — раскладка реестра (`MenuLayout`), формат, разбор аргументов —
  покрыто; остальное проверяется на живой Windows.

## CLI

- `-i`, `--install` — регистрация; `-r`, `-remove`, `--uninstall`, `-u` — удаление;
  `-v`, `--version`; `-h`, `--help`; `nomedia <папка>`; `deskedit <папка>`;
  `set-deskedit <путь>`. Разбор — `CommandLine.Parse`, алиасы покрыты тестами.
- exe собирается как WinExe, чтобы меню не мигало консолью. Из-за этого `cmd` его не
  ждёт: в bat-файлах запуск идёт через `start /wait`, а сам exe цепляется к консоли
  родителя (`ConsoleSupport.TryAttach`), иначе печатать некуда.

## Что это

`FolderTools` — утилита для Windows: добавляет в контекстное меню папки пункт с
подпунктами «Создать .nomedia» и «desktop.ini (DeskEdit)».

## Структура

- `src/FolderTools.Core` — netstandard2.0, вся тестируемая логика (без WinForms).
- `src/FolderTools.App` — net48, WinForms: точка входа и работа с реестром.
- `src/FolderTools.Tests` — net8.0, xUnit; ссылается только на Core.
- `assets/app.ico` — генерируется `tools/make_icon.py` (без сторонних библиотек).
- `installer/` — `install.bat`, `uninstall.bat` (вызывают `FolderTools.exe install|uninstall`).

## Сборка и тесты

```
dotnet build FolderTools.sln -c Release
dotnet test src/FolderTools.Tests/FolderTools.Tests.csproj
```

- Логику (Core/Tests) можно прогонять на Linux; `FolderTools.App` (net48) собирается
  только на Windows. CI — `windows-latest`.
- На Linux без ICU нужен `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

## Соглашения и подводные камни

- Формат строки `.nomedia` не должен зависеть от региональных настроек: дни недели
  жёстко заданы в `NomediaFormatter`, формат собирается через `InvariantCulture`.
- Источники хранятся в UTF-8 без BOM с кириллицей, поэтому в `Directory.Build.props`
  задано `<CodePage>65001</CodePage>`.
- Пункт меню регистрируется в `HKCU\Software\Classes` (без прав администратора) для
  `Directory\Background` (пустое место в папке), `Directory` (иконка папки) и `Drive`.
  Каскадное подменю описано в `MenuLayout` и пишется в двух формах, которые понимает
  проводник: значение `ExtendedSubCommandsKey` на общий набор команд
  (`FolderTools.Menu`) и одноимённый подраздел с командами внутри.
- В командах меню используется `%V` (папка). `%1` для клика по пустому месту пустой,
  поэтому не подходит.
- DeskEdit не встраивается в поставку; путь задаётся `set-deskedit` или ищется рядом
  с exe / в `C:\Tools\DeskEdit`.

## Копировать в / Переместить в

- Пункты статичные, без COM-расширения оболочки: фильтрация по расширению сделана
  через `AppliesTo` с синтаксисом AQS (`System.FileName:~"*.txt"`), где `*` — любая
  последовательность, `?` — ровно один символ. Пользовательский `!` транслируется
  в `?` функцией `ExtensionPattern.ToShellWildcard`.
- `AppliesTo` не умеет отличать папку от файла, поэтому назначения для папок
  (`[папка]`) регистрируются отдельно — под `Directory`, а не под `*`.
- Команда пункта: `copyto <id> "%1"`. Оболочка вызывает статичный глагол по разу на
  каждый выделенный объект, поэтому `%1` — всегда один путь и маршрутизация по
  расширению точная. `MultiSelectModel=Player` снимает лимит в 15 объектов.
- Шаблоны, начинающиеся с `*` или `.`, сравниваются с расширением (`*.txt` ловит и
  `archive.tar.txt`); остальные (`фото!.jpg`) — с полным именем. Это должно совпадать
  с тем, что показывает `AppliesTo`, иначе пункт виден, но ничего не делает.
- Файлы никогда не перезаписываются: при совпадении имени добавляется ` (2)`.
  Перемещение останавливается на первой ошибке, чтобы не потерять данные молча.
- Настройки — `FolderTools.settings.json` рядом с exe, пишутся атомарно через
  `File.Replace`. Меню перестраивается при каждом сохранении (`RefreshMenu`).
- Пути собираются через `PathUtil` (`Combine`, `GetLeafName`, `ToNative`), а не через
  `Path.Combine`: тесты идут на Linux, где `\` — обычный символ имени файла.
