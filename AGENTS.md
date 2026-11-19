# AGENTS.md

## Репозиторий

- GitHub: `faustpronis/windows-context-menu-.nomedia` (public). Работа идёт в `main`.
- CI: `.github/workflows/build.yml` на `windows-latest` — build + test + артефакт
  `FolderTools.zip`, релиз на тег `v*`. Прогон на `main` зелёный.
- `dist/FolderTools.zip` — архив, собранный CI, лежит в репозитории намеренно, чтобы
  утилиту можно было скачать без похода в Actions. При изменениях кода его нужно
  пересобрать (`dotnet build -c Release` + упаковка) и обновить.
- Проверить меню в проводнике в этой среде нельзя (нет Windows). Всё, что можно
  покрыть тестами — раскладка реестра (`MenuLayout`), формат, разбор аргументов —
  покрыто; остальное проверяется на живой Windows.

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
