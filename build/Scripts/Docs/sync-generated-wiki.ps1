param(
    [Parameter(Mandatory)] [string] $SourcePath,
    [Parameter(Mandatory)] [string] $WikiPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Wiki.Common.ps1')

$sourceRoot = (Resolve-Path -LiteralPath $SourcePath).Path
$wikiRoot = (Resolve-Path -LiteralPath $WikiPath).Path
Assert-WikiUnlinkedPath $sourceRoot
Assert-WikiUnlinkedPath $wikiRoot
if ((Test-WikiPathInside $sourceRoot $wikiRoot) -or (Test-WikiPathInside $wikiRoot $sourceRoot)) {
    throw 'Generated output and wiki checkout must be separate directories.'
}
if (!(Test-Path -LiteralPath (Join-Path $wikiRoot '.git'))) { throw 'WikiPath must be a cloned wiki Git checkout.' }
$files = @(Read-WikiManagedFiles $sourceRoot -Required)
if ($files.Count -eq 0 -or 'Home.md' -notin $files -or '_Sidebar.md' -notin $files) {
    throw 'Generated wiki output must contain Home.md and _Sidebar.md.'
}
$pages = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in $files) {
    $path = Get-WikiManagedPath $sourceRoot $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Generated wiki page is missing: $name" }
    Assert-WikiOwnedPage $path
    $pages.Add($name, [IO.File]::ReadAllText($path).Replace("`r`n", "`n"))
}
Write-WikiManagedPages $wikiRoot $pages -AllowInitialPages
Write-Host "Synced $($pages.Count) generated wiki files; other wiki pages were preserved."
