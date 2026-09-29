# Reformats the solution in the parent repo using two tools, in order:
#   1. `jb cleanupcode` (JetBrains ReSharper Command Line Tools, free) - runs a custom
#      "GoldenExample" cleanup profile (see Lint\CodeStyle.DotSettings) that ONLY reformats code
#      (braces, initializers, empty blocks, attribute placement, etc.) - it deliberately does NOT
#      run the built-in "Full Cleanup" profile, which would also remove "redundant" braces,
#      optimize usings, rewrite doc comments, and more.
#   2. `dotnet format whitespace` (SDK-bundled) - a final indentation/whitespace-only pass.
#
# NOTE: The standalone/global `dotnet-format` tool (installed via
# `dotnet tool install -g dotnet-format`) is NOT used here on purpose: its latest published
# version (5.1.250801) corrupts indentation on classes that combine a primary constructor with a
# generic interface in the base list (e.g. `class Foo(Bar bar) : IEndpoint<Baz>`), which this
# codebase uses extensively. The SDK's built-in `dotnet format` command handles this correctly, so
# it is used instead.
#
# Known limitation (both tools): neither will collapse an already hand-wrapped multi-line member
# declaration (e.g. a primary constructor with one parameter per line) back onto a single line -
# formatters only add line breaks where required, they never remove an author's existing ones.
# Fix such cases manually if desired.
#
# This whole Lint folder is portable: copy it into another repo, run .\Lint\install.ps1 once (to
# deploy Lint\.editorconfig to that repo's root - see install.ps1 for why this can't be avoided),
# then use .\Lint\format.ps1 there exactly as here.
#
# Usage:
#   .\format.ps1                       # reformat the solution auto-detected in the repo root
#   .\format.ps1 -Verify               # check formatting only, fail if changes are needed
#   .\format.ps1 -Solution path\to.sln # target a specific solution/project file

param(
	[switch]$Verify,
	[string]$Solution
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$settings = Join-Path $PSScriptRoot "CodeStyle.DotSettings"

if (-not (Test-Path $settings)) {
	throw "DotSettings file not found: $settings"
}

if ($Solution) {
	$solution = $Solution
} else {
	$candidates = @(Get-ChildItem -Path $repoRoot -Filter "*.slnx" -File) + @(Get-ChildItem -Path $repoRoot -Filter "*.sln" -File)

	if ($candidates.Count -eq 0) {
		throw "No .slnx or .sln file found in $repoRoot. Pass one explicitly with -Solution."
	}

	if ($candidates.Count -gt 1) {
		throw "Multiple solution files found in $repoRoot ($($candidates.Name -join ', ')). Pass one explicitly with -Solution."
	}

	$solution = $candidates[0].FullName
}

if (-not (Test-Path $solution)) {
	throw "Solution file not found: $solution"
}

if ($Verify) {
	# jb cleanupcode has no "verify only" mode, so verification relies on dotnet format's
	# --verify-no-changes, which checks whitespace/indentation - the same properties jb
	# cleanupcode's rules ultimately normalize to as well.
	Write-Host "Running: dotnet format whitespace $solution --verify-no-changes"
	& dotnet format whitespace $solution --verify-no-changes
	$exitCode = $LASTEXITCODE

	if ($exitCode -ne 0) {
		Write-Host "Formatting check failed - run '.\Lint\format.ps1' (without -Verify) to fix." -ForegroundColor Red
		exit $exitCode
	}

	Write-Host "Formatting check passed." -ForegroundColor Green
	exit 0
}

$jb = Get-Command "jb" -ErrorAction SilentlyContinue

if ($null -eq $jb) {
	Write-Host "'jb' (JetBrains ReSharper Command Line Tools) not found on PATH." -ForegroundColor Yellow
	Write-Host "Install it with: dotnet tool install -g JetBrains.ReSharper.GlobalTools" -ForegroundColor Yellow
	Write-Host "Skipping the jb cleanupcode step, falling back to whitespace-only formatting." -ForegroundColor Yellow
} else {
	Write-Host "Running: jb cleanupcode --profile=GoldenExample --settings=$settings $solution"
	& jb cleanupcode --profile="GoldenExample" --settings="$settings" $solution
	$exitCode = $LASTEXITCODE

	if ($exitCode -ne 0) {
		Write-Host "jb cleanupcode failed with exit code $exitCode" -ForegroundColor Red
		exit $exitCode
	}
}

Write-Host "Running: dotnet format whitespace $solution"
& dotnet format whitespace $solution
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
	Write-Host "dotnet format failed with exit code $exitCode" -ForegroundColor Red
	exit $exitCode
}

Write-Host "Formatting complete." -ForegroundColor Green
