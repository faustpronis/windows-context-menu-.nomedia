@echo off
rem Removes the FolderTools entry from the folder context menu.
setlocal
set "EXE=%~dp0FolderTools.exe"
if not exist "%EXE%" (
  echo FolderTools.exe not found next to this script.
  exit /b 1
)
rem Remove runtime DLLs left beside the exe by older releases; current DLLs live
rem in FolderTools_dlls and are not matched by this top-level wildcard.
for %%F in ("%~dp0*.dll") do if exist "%%~fF" del /f /q "%%~fF"
start /wait "" "%EXE%" -r
endlocal
