@echo off
REM Reformats the solution in the parent repo.
REM See format.ps1 in this folder for details on the tools used (jb cleanupcode + dotnet format)
REM and why the standalone `dotnet-format` global tool is bypassed.
REM This Lint folder is portable - see README.md for how to copy it into another repo.
REM
REM Usage:
REM   format.bat            reformat the auto-detected solution in the repo root
REM   format.bat verify     check formatting only, fail if changes are needed (no files touched)

setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_ARGS="

if /I "%~1"=="verify" (
	set "PS_ARGS=-Verify"
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%format.ps1" %PS_ARGS%
exit /b %ERRORLEVEL%
