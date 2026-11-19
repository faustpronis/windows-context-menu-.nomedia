# FolderTools

[![release](https://img.shields.io/github/v/release/faustpronis/windows-context-menu-.nomedia?display_name=tag&sort=semver)](https://github.com/faustpronis/windows-context-menu-.nomedia/releases/latest)

Небольшая утилита для Windows, которая добавляет в контекстное меню папки пункт
**FolderTools** с подпунктами:

1. **Создать .nomedia** — создаёт (или дополняет) в текущей папке файл `.nomedia`
   с отметкой времени и путём этой папки.
2. **desktop.ini (DeskEdit)** — открывает текущую папку в редакторе `desktop.ini`
   [Desktop.ini Editor](https://www.sordum.org/10084/desktop-ini-editor-v1-2/) (DeskEdit).

Пункт появляется при клике правой кнопкой по папке, по пустому месту внутри папки
и по корню диска. Регистрация идёт в `HKEY_CURRENT_USER`, права администратора не нужны.

## Что пишется в .nomedia

Первая строка — время и дата, вторая — путь к папке:

```
18:53:23  18.11.2026 Ср
D:\SHARE\SYNCTHING\
```

Правила:

- Формат даты и дня недели не зависит от региональных настроек Windows:
  дни недели жёстко заданы (`Пн`…`Вс`), формат — `ЧЧ:ММ:СС  ДД.ММ.ГГГГ`.
- Если файла `.nomedia` нет — он создаётся.
- Если файл есть, но записи для этой папки в нём нет — новая запись добавляется
  в конец, отделённая пустой строкой.
- Если запись для этой папки уже есть (путь сравнивается без учёта регистра
  и типа разделителей) — ничего не меняется.
- Кодировка — UTF-8 без BOM, перевод строки — CRLF.

## Скачать

Готовый бинарник лежит в [Releases](https://github.com/faustpronis/windows-context-menu-.nomedia/releases/latest):
архив `FolderTools-<версия>.zip`. Текущая версия — **0.1.0**.

## Установка

1. Распакуйте архив целиком (нужны и `FolderTools.exe`, и `FolderTools.Core.dll`),
   например в `C:\Tools\FolderTools`. Папку потом не перемещайте: путь к exe
   записывается в реестр при регистрации.
2. Зарегистрируйте пункт меню:

   ```
   FolderTools.exe -i
   ```

   или двойным кликом по `install.bat`.

3. Правый клик по папке (или по пустому месту внутри неё) → **FolderTools**.

Удаление — `FolderTools.exe -r` или `uninstall.bat`.

## DeskEdit (desktop.ini)

Утилита не включает в себя DeskEdit. Скачайте его со страницы
<https://www.sordum.org/10084/desktop-ini-editor-v1-2/>, распакуйте, например в
`C:\Tools\DeskEdit`, и укажите путь:

```
FolderTools.exe set-deskedit "C:\Tools\DeskEdit\DeskEdit_x64.exe"
```

Утилита ищет редактор в таком порядке: значение `DeskEditPath` в реестре, файл
рядом с `FolderTools.exe`, `C:\Tools\DeskEdit\DeskEdit_x64.exe` и
`C:\Tools\DeskEdit\DeskEdit.exe`.

Редактор запускается с путём папки в качестве параметра
(`DeskEdit_x64.exe "D:\My_Folder"`) — такую форму поддерживает сам DeskEdit.

## Команды

| Команда | Действие |
|---|---|
| `FolderTools.exe -i`, `--install` | Зарегистрировать пункт в контекстном меню папок |
| `FolderTools.exe -r`, `-remove`, `--uninstall` | Убрать пункт из контекстного меню |
| `FolderTools.exe -v`, `--version` | Показать версию |
| `FolderTools.exe nomedia "<папка>"` | Создать или дополнить `.nomedia` |
| `FolderTools.exe deskedit "<папка>"` | Открыть `desktop.ini` папки в DeskEdit |
| `FolderTools.exe set-deskedit "<путь>"` | Запомнить путь к `DeskEdit.exe` |
| `FolderTools.exe -h`, `--help` | Справка |

Без аргументов показывается справка. Если утилиту запустили из консоли, результат
печатается в неё; при запуске двойным кликом — показывается окно.

Настройки хранятся в `HKCU\Software\FolderTools`:

- `DeskEditPath` — путь к `DeskEdit.exe`.
- `ShowSuccess` (DWORD) = `1` — показывать окно подтверждения после создания
  `.nomedia`. По умолчанию окна нет: результат видно в самой папке.

## Сборка

```
dotnet build FolderTools.sln -c Release
dotnet test src/FolderTools.Tests/FolderTools.Tests.csproj
```

Приложение — это .NET Framework 4.8 (WinForms), поэтому собирается на Windows.
Тесты и ядро — на .NET 8 / netstandard2.0, их можно прогонять и на Linux.
Иконка приложения генерируется скриптом `tools/make_icon.py` в `assets/app.ico`.

CI (GitHub Actions, `.github/workflows/build.yml`) собирает проект и прогоняет тесты.
При пуше тега вида `v0.1.0` версия из тега подставляется в сборку, проверяется,
что она попала в exe, и публикуется релиз с архивом `FolderTools-<версия>.zip`.
При обычном пуше в `main` тот же архив лежит в артефактах прогона.

## Структура

```
src/FolderTools.Core     логика: формат времени, .nomedia, запуск DeskEdit, разбор аргументов
src/FolderTools.App      WinForms-приложение и регистрация пункта меню
src/FolderTools.Tests    тесты (xUnit)
installer/               install.bat / uninstall.bat
tools/make_icon.py       генератор assets/app.ico
```
