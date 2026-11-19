@echo off
rem Removes the FolderTools entry from the folder context menu.
setlocal
set "EXE=%~dp0FolderTools.exe"
if not exist "%EXE%" (
  echo FolderTools.exe not found next to this script.
  exit /b 1
)
"%EXE%" uninstall
echo.
echo FolderTools has been removed from the folder context menu.
endlocal
