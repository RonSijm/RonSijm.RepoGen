@echo off
REM Deploys this Lint folder's canonical .editorconfig to the repo root.
REM Run this once after copying the Lint folder into a new repo. See install.ps1 for details.

setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%install.ps1"
exit /b %ERRORLEVEL%
