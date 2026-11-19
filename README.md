# FolderTools

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

## Установка

1. Скопируйте папку с утилитой, например в `C:\Tools\FolderTools`.
2. Запустите `install.bat` (или `FolderTools.exe install`) — пункт появится в
   контекстном меню папок.

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
| `FolderTools.exe nomedia "<папка>"` | Создать или дополнить `.nomedia` |
| `FolderTools.exe deskedit "<папка>"` | Открыть `desktop.ini` папки в DeskEdit |
| `FolderTools.exe install` | Добавить пункт в контекстное меню |
| `FolderTools.exe uninstall` | Убрать пункт из контекстного меню |
| `FolderTools.exe set-deskedit "<путь>"` | Запомнить путь к `DeskEdit.exe` |

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

CI (GitHub Actions, `.github/workflows/build.yml`) собирает проект, прогоняет тесты
и на тег вида `v1.0.0` публикует релиз с архивом `FolderTools.zip`.

## Структура

```
src/FolderTools.Core     логика: формат времени, .nomedia, запуск DeskEdit, разбор аргументов
src/FolderTools.App      WinForms-приложение и регистрация пункта меню
src/FolderTools.Tests    тесты (xUnit)
installer/               install.bat / uninstall.bat
tools/make_icon.py       генератор assets/app.ico
```
