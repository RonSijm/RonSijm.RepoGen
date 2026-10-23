# Deploys this Lint folder's canonical ".editorconfig" to the repo root.
#
# EditorConfig files are discovered purely by directory hierarchy (from the file being formatted,
# walking up to the nearest ancestor .editorconfig with `root = true`) - there is no way to pass a
# path to one explicitly, unlike CodeStyle.DotSettings, which IS passed explicitly via --settings.
# So Lint/.editorconfig can't be used in place directly; it must be copied to the repo root.
#
# To adopt this Lint folder's style in another repo:
#   1. Copy this entire "Lint" folder into the target repo.
#   2. From the target repo root, run: .\Lint\install.ps1
#   3. Use .\Lint\format.bat / .\Lint\format.bat verify as normal.
#
# Re-run this script any time Lint/.editorconfig changes, to keep the deployed root copy in sync.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $PSScriptRoot ".editorconfig"
$destination = Join-Path $repoRoot ".editorconfig"

if (-not (Test-Path $source)) {
	throw "Canonical .editorconfig not found: $source"
}

Copy-Item -Path $source -Destination $destination -Force
Write-Host "Deployed $source -> $destination" -ForegroundColor Green

$jb = Get-Command "jb" -ErrorAction SilentlyContinue
if ($null -eq $jb) {
	Write-Host "'jb' (JetBrains ReSharper Command Line Tools) not found on PATH." -ForegroundColor Yellow
	Write-Host "Install it with: dotnet tool install -g JetBrains.ReSharper.GlobalTools" -ForegroundColor Yellow
} else {
	Write-Host "'jb' found on PATH - ready to use .\Lint\format.bat" -ForegroundColor Green
}
