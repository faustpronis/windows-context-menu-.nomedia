@echo off
rem Registers the FolderTools entry in the folder context menu (HKCU, no admin rights needed).
setlocal
set "EXE=%~dp0FolderTools.exe"
if not exist "%EXE%" (
  echo FolderTools.exe not found next to this script.
  exit /b 1
)
rem Remove runtime DLLs left beside the exe by older releases when the ZIP was
rem extracted over the existing installation. Current DLLs live in FolderTools_dlls.
for %%F in ("%~dp0*.dll") do if exist "%%~fF" del /f /q "%%~fF"
rem The tool is a WinExe, so cmd would not wait for it; /wait keeps the output ordered.
start /wait "" "%EXE%" -i
endlocal
