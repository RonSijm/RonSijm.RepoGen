# Reformats the solution in the parent repo using four passes, in order:
#   1. `dotnet format style --diagnostics IDE0011 IDE0211` (SDK-bundled) - adds required
#      braces and converts top-level statements to Program.Main without unrelated style fixes.
#   2. `dotnet format analyzers --diagnostics CA1852` - seals internal classes in Program.cs
#      so newly converted entry points satisfy the existing build analyzer.
#   3. `jb cleanupcode` (JetBrains ReSharper Command Line Tools, free) - runs a custom
#      "GoldenExample" cleanup profile (see Lint\CodeStyle.DotSettings) that ONLY reformats code
#      (braces, initializers, empty blocks, attribute placement, etc.) - it deliberately does NOT
#      run the built-in "Full Cleanup" profile, which would also remove "redundant" braces,
#      optimize usings, rewrite doc comments, and more.
#   4. `dotnet format whitespace` (SDK-bundled) - a final indentation/whitespace-only pass.
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

$solution = (Resolve-Path -LiteralPath $solution).Path

function Invoke-ProgramTypeCleanup {
	param([switch]$Check)

	$arguments = @("format", "analyzers", $solution, "--diagnostics", "CA1852", "--include", "**\Program.cs")
	if ($Check) {
		$arguments += "--verify-no-changes"
	}

	Write-Host "Running: dotnet $($arguments -join ' ')"
	# dotnet format's include patterns only match files under its working directory.
	Push-Location -LiteralPath $repoRoot
	try {
		& dotnet @arguments
		$exitCode = $LASTEXITCODE
	} finally {
		Pop-Location
	}

	if ($exitCode -ne 0) {
		Write-Host "Program type enforcement failed with exit code $exitCode" -ForegroundColor Red
		exit $exitCode
	}
}

if ($Verify) {
	# jb cleanupcode has no "verify only" mode, so verification relies on dotnet format's
	# --verify-no-changes to check code style, Program types and whitespace/indentation.
	Write-Host "Running: dotnet format style $solution --diagnostics IDE0011 IDE0211 --verify-no-changes"
	& dotnet format style $solution --diagnostics IDE0011 IDE0211 --verify-no-changes
	$exitCode = $LASTEXITCODE

	if ($exitCode -ne 0) {
		Write-Host "Code-style check failed - run '.\Lint\format.ps1' (without -Verify) to fix." -ForegroundColor Red
		exit $exitCode
	}

	Invoke-ProgramTypeCleanup -Check

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

Write-Host "Running: dotnet format style $solution --diagnostics IDE0011 IDE0211"
& dotnet format style $solution --diagnostics IDE0011 IDE0211
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
	Write-Host "Code-style enforcement failed with exit code $exitCode" -ForegroundColor Red
	exit $exitCode
}

Invoke-ProgramTypeCleanup

$jb = Get-Command "jb" -ErrorAction SilentlyContinue

if ($null -eq $jb) {
	Write-Host "'jb' (JetBrains ReSharper Command Line Tools) not found on PATH." -ForegroundColor Yellow
	Write-Host "Install it with: dotnet tool install -g JetBrains.ReSharper.GlobalTools" -ForegroundColor Yellow
	Write-Host "Skipping the jb cleanupcode step; code-style enforcement has completed." -ForegroundColor Yellow
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
