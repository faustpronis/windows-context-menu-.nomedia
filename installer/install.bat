@echo off
rem Registers the FolderTools entry in the folder context menu (HKCU, no admin rights needed).
setlocal
set "EXE=%~dp0FolderTools.exe"
if not exist "%EXE%" (
  echo FolderTools.exe not found next to this script.
  exit /b 1
)
"%EXE%" install
echo.
echo FolderTools has been added to the folder context menu.
echo Right-click a folder (or empty space inside it) to see the menu.
endlocal
