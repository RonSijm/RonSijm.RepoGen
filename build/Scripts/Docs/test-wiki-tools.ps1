$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Wiki.Common.ps1')

$script:wikiChecks = 0
function Assert-WikiTest {
    param([bool] $Condition, [string] $Message)
    if (!$Condition) { throw "Wiki test failed: $Message" }
    $script:wikiChecks++
}
function Assert-WikiThrows {
    param([scriptblock] $Action, [string] $Expected)
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_ }
    Assert-WikiTest ($null -ne $failure -and $failure.ToString().Contains($Expected)) "Expected failure containing '$Expected', got '$failure'"
}
function Write-WikiTestFile {
    param([string] $Path, [string] $Content)
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path, $Content.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
function Get-WikiTestSnapshot {
    param([string] $Root)
    return @(Get-ChildItem -LiteralPath $Root -File -Force | Sort-Object Name | ForEach-Object {
        $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }) -join [char]10
}

$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $tempParent ('repogen-wiki-tests-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
try {
    $source = Join-Path $fixture 'source'
    $output = Join-Path $fixture 'output'
    $wiki = Join-Path $fixture 'wiki'
    $exporter = Join-Path $PSScriptRoot 'export-docs-to-wiki.ps1'
    $sync = Join-Path $PSScriptRoot 'sync-generated-wiki.ps1'
    $fence = ([string][char]96) * 3
    $homeContent = @(
        '# Example'
        '[Docs](docs/README.md)'
        '[Guide](docs/implemented/queries/Guide.md#guide "Guide title")'
        '[Folder](docs/implemented/queries)'
        '[Code](src/example.cs:2-3)'
        '[Spaced](<src/file%20with%20spaces.cs>)'
        '[Example](samples/Demo)'
        '![Image](assets/picture.svg)'
        '[ref]: docs/implemented/queries/Guide.md#guide "Reference title"'
        '[image-ref]: assets/picture.svg'
        '[Remote](https://example.com/document)'
        '[Anchor](#example)'
        $fence + 'text'
        '[Literal](docs/does-not-exist.md)'
        $fence
    ) -join [char]10
    Write-WikiTestFile (Join-Path $source 'README.md') ($homeContent + [char]10)
    Write-WikiTestFile (Join-Path $source 'docs/README.md') "# Documentation`n[Home](../README.md)`n"
    foreach ($section in @('implemented', 'work-in-progress', 'future', 'other')) {
        Write-WikiTestFile (Join-Path $source "docs/$section/README.md") "# $section`n"
    }
    Write-WikiTestFile (Join-Path $source 'docs/implemented/queries/README.md') "# Queries`n[Guide](Guide.md)`n"
    Write-WikiTestFile (Join-Path $source 'docs/implemented/queries/Guide.md') "# Guide`n[Docs](../../README.md)`n"
    Write-WikiTestFile (Join-Path $source 'docs/future/Old.md') "# Old`n"
    Write-WikiTestFile (Join-Path $source 'src/example.cs') "// First`n// Second`n// Third`n"
    Write-WikiTestFile (Join-Path $source 'src/file with spaces.cs') '// Space'
    Write-WikiTestFile (Join-Path $source 'samples/Demo/README.md') '# Demo'
    Write-WikiTestFile (Join-Path $source 'assets/picture.svg') '<svg xmlns="http://www.w3.org/2000/svg"/>'

    & $exporter -SourceRoot $source -OutputPath $output -Repository owner/repo -RefName 'feature/docs'
    $homePage = [IO.File]::ReadAllText((Join-Path $output 'Home.md'))
    foreach ($expected in @(
        '[Docs](Documentation)',
        '[Guide](implemented-queries-Guide#guide "Guide title")',
        '[Folder](implemented-queries)',
        '[Code](https://github.com/owner/repo/blob/feature%2Fdocs/src/example.cs#L2-L3)',
        '[Spaced](https://github.com/owner/repo/blob/feature%2Fdocs/src/file%20with%20spaces.cs)',
        '[Example](https://github.com/owner/repo/tree/feature%2Fdocs/samples/Demo)',
        '![Image](https://github.com/owner/repo/raw/feature%2Fdocs/assets/picture.svg)',
        '[ref]: implemented-queries-Guide#guide "Reference title"',
        '[image-ref]: https://github.com/owner/repo/raw/feature%2Fdocs/assets/picture.svg',
        '[Remote](https://example.com/document)',
        '[Anchor](#example)',
        '[Literal](docs/does-not-exist.md)'
    )) { Assert-WikiTest ($homePage.Contains($expected)) "Converted link/code: $expected" }
    Assert-WikiTest ($homePage.StartsWith($script:RepoGenWikiMarker)) 'Ownership marker'
    Assert-WikiTest ([IO.File]::ReadAllText((Join-Path $source 'README.md')) -ceq ($homeContent + [char]10)) 'Source README is untouched'
    Assert-WikiTest (([IO.File]::ReadAllText((Join-Path $output 'Documentation.md'))).Contains('[Home](Home)')) 'Docs index links to wiki Home'
    $sidebar = [IO.File]::ReadAllText((Join-Path $output '_Sidebar.md'))
    foreach ($expected in @('[implemented](implemented)', '  - [Queries](implemented-queries)', '[work-in-progress](work-in-progress)', '[future](future)', '[other](other)')) {
        Assert-WikiTest ($sidebar.Contains($expected)) "Sidebar section: $expected"
    }
    $snapshot = Get-WikiTestSnapshot $output
    & $exporter -SourceRoot $source -OutputPath $output -Repository owner/repo -RefName 'feature/docs'
    Assert-WikiTest ((Get-WikiTestSnapshot $output) -eq $snapshot) 'Repeat export is byte-identical'

    [void][IO.Directory]::CreateDirectory((Join-Path $wiki '.git'))
    Write-WikiTestFile (Join-Path $wiki 'Home.md') '# Initial GitHub wiki page'
    Write-WikiTestFile (Join-Path $wiki 'Manual.md') '# Handwritten wiki page'
    & $sync -SourcePath $output -WikiPath $wiki
    Assert-WikiTest ([IO.File]::ReadAllText((Join-Path $wiki 'Manual.md')) -eq '# Handwritten wiki page') 'Handwritten page preserved'
    Assert-WikiTest ([IO.File]::ReadAllText((Join-Path $wiki 'Home.md')) -eq $homePage) 'Bootstrap Home replaced'
    $wikiSnapshot = Get-WikiTestSnapshot $wiki
    & $sync -SourcePath $output -WikiPath $wiki
    Assert-WikiTest ((Get-WikiTestSnapshot $wiki) -eq $wikiSnapshot) 'Repeat sync is byte-identical'
    $oldSource = Join-Path $source 'docs/future/Old.md'
    Assert-WikiTest (Test-WikiPathInside $fixture $oldSource) 'Stale source deletion stays in fixture'
    Remove-Item -LiteralPath $oldSource
    & $exporter -SourceRoot $source -OutputPath $output -Repository owner/repo -RefName 'feature/docs'
    & $sync -SourcePath $output -WikiPath $wiki
    Assert-WikiTest (!(Test-Path -LiteralPath (Join-Path $output 'future-Old.md'))) 'Stale generated output removed'
    Assert-WikiTest (!(Test-Path -LiteralPath (Join-Path $wiki 'future-Old.md'))) 'Stale wiki page removed'
    Assert-WikiTest (Test-Path -LiteralPath (Join-Path $wiki 'Manual.md')) 'Stale cleanup keeps manual pages'

    $safeSnapshot = Get-WikiTestSnapshot $output
    Assert-WikiThrows { & $exporter -SourceRoot $source -OutputPath $source } 'must not overwrite'
    Assert-WikiThrows { & $exporter -SourceRoot $source -OutputPath (Join-Path $source 'docs/export') } 'must not overwrite'
    Assert-WikiThrows { & $exporter -SourceRoot $source -OutputPath $fixture } 'must not overwrite'
    Write-WikiTestFile (Join-Path $source 'docs/Collision-Page.md') '# One'
    Write-WikiTestFile (Join-Path $source 'docs/Collision/Page.md') '# Two'
    Assert-WikiThrows { & $exporter -SourceRoot $source -OutputPath $output } 'collision'
    Remove-Item -LiteralPath (Join-Path $source 'docs/Collision-Page.md'), (Join-Path $source 'docs/Collision/Page.md')
    Write-WikiTestFile (Join-Path $source 'docs/Bad.md') "# Bad`n[Missing](Missing.md)`n"
    Assert-WikiThrows { & $exporter -SourceRoot $source -OutputPath $output } 'Missing documentation link'
    Write-WikiTestFile (Join-Path $source 'docs/Bad.md') "# Bad`n[Outside](../../outside.md)`n"
    Assert-WikiThrows { & $exporter -SourceRoot $source -OutputPath $output } 'leaves the source repository'
    Remove-Item -LiteralPath (Join-Path $source 'docs/Bad.md')
    Assert-WikiTest ((Get-WikiTestSnapshot $output) -eq $safeSnapshot) 'Failed export does not alter output'

    $manifestPath = Join-Path $wiki $script:RepoGenWikiManifest
    $manifest = [IO.File]::ReadAllText($manifestPath)
    Write-WikiTestFile (Join-Path $fixture 'outside.md') '# Outside must survive'
    Write-WikiTestFile $manifestPath "../outside.md`n"
    Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $wiki } 'Invalid generated wiki filename'
    Assert-WikiTest ([IO.File]::ReadAllText((Join-Path $fixture 'outside.md')) -eq '# Outside must survive') 'Manifest traversal never deletes outside'
    Write-WikiTestFile $manifestPath $manifest
    Write-WikiTestFile (Join-Path $wiki 'other.md') '# Handwritten collision'
    Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $wiki } 'handwritten wiki page'
    Assert-WikiTest ([IO.File]::ReadAllText((Join-Path $wiki 'other.md')) -eq '# Handwritten collision') 'Collision is not overwritten'
    Remove-Item -LiteralPath (Join-Path $wiki 'other.md')
    $generatedManifest = Join-Path $output $script:RepoGenWikiManifest
    $generatedNames = [IO.File]::ReadAllText($generatedManifest)
    Write-WikiTestFile $generatedManifest "../outside.md`n"
    Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $wiki } 'Invalid generated wiki filename'
    Write-WikiTestFile $generatedManifest $generatedNames
    Write-WikiTestFile $generatedManifest ($generatedNames + "Missing.md`n")
    Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $wiki } 'page is missing'
    Write-WikiTestFile $generatedManifest $generatedNames
    Write-WikiTestFile $generatedManifest ($generatedNames + "Home.md`n")
    Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $wiki } 'Duplicate generated wiki filename'
    Write-WikiTestFile $generatedManifest $generatedNames
    Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $output } 'separate directories'
    Assert-WikiThrows { Get-WikiManagedPath $output 'CON.md' } 'Invalid generated wiki filename'
    $manifestDirectoryWiki = Join-Path $fixture 'manifest-directory-wiki'
    [void][IO.Directory]::CreateDirectory((Join-Path $manifestDirectoryWiki '.git'))
    [void][IO.Directory]::CreateDirectory((Join-Path $manifestDirectoryWiki $script:RepoGenWikiManifest))
    Write-WikiTestFile (Join-Path $manifestDirectoryWiki 'Home.md') '# Untouched initial page'
    Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $manifestDirectoryWiki } 'not a regular file'
    Assert-WikiTest ([IO.File]::ReadAllText((Join-Path $manifestDirectoryWiki 'Home.md')) -eq '# Untouched initial page') 'Bad manifest fails before replacing Home'
    if (!$IsWindows) {
        $link = Join-Path $wiki 'implemented-queries-Guide.md'
        Remove-Item -LiteralPath $link
        [void](New-Item -ItemType SymbolicLink -Path $link -Target (Join-Path $fixture 'outside.md'))
        Assert-WikiThrows { & $sync -SourcePath $output -WikiPath $wiki } 'Linked paths'
        Assert-WikiTest ([IO.File]::ReadAllText((Join-Path $fixture 'outside.md')) -eq '# Outside must survive') 'Symlink target is untouched'
    }
    Write-Host "Wiki tooling: $script:wikiChecks checks passed (Linux also checks symlink rejection)."
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (!(Test-WikiPathInside $tempParent $resolvedFixture) -or [IO.Path]::GetFileName($resolvedFixture) -notmatch '^repogen-wiki-tests-[a-f0-9]{32}$') {
        throw 'Refusing to clean an unexpected test directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
