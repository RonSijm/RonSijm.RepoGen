param(
    [string] $OutputPath,
    [string] $Repository = $(if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'RonSijm/RonSijm.RepoGen' }),
    [string] $RefName = $(if ($env:GITHUB_SHA) { $env:GITHUB_SHA } elseif ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { 'main' }),
    [string] $SourceRoot = (Join-Path $PSScriptRoot '../../..')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Wiki.Common.ps1')

if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or [string]::IsNullOrWhiteSpace($RefName)) {
    throw 'Supply a repository in owner/name form and a non-empty source ref.'
}
$repositoryRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$docsRoot = Join-Path $repositoryRoot 'docs'
Assert-WikiUnlinkedPath $repositoryRoot
if (!(Test-Path -LiteralPath $docsRoot -PathType Container) -or !(Test-Path -LiteralPath (Join-Path $repositoryRoot 'README.md') -PathType Leaf)) {
    throw 'Wiki input requires a repository README.md and a docs directory.'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repositoryRoot 'build/Artifacts/Wiki' }
$outputRoot = [IO.Path]::GetFullPath($OutputPath)
if ((Test-WikiPathInside $outputRoot $repositoryRoot) -or (Test-WikiPathInside $docsRoot $outputRoot)) {
    throw 'Wiki output must not overwrite the repository root, an ancestor, or the source docs.'
}
Assert-WikiUnlinkedPath $outputRoot
$encodedRef = [Uri]::EscapeDataString($RefName)

function Get-WikiPageName {
    param([string] $RelativePath)
    if ($RelativePath -eq 'README.md') { return 'Home.md' }
    if ($RelativePath -eq 'docs/README.md') { return 'Documentation.md' }
    $name = $RelativePath.Substring(5) -replace '(?i)(?:/README)?\.md$', ''
    return (($name -replace '[/\\]', '-') -replace '[^A-Za-z0-9._-]', '-') + '.md'
}

function Get-WikiTitle {
    param([string] $Content, [string] $Fallback)
    $heading = [regex]::Match($Content, '(?m)^#\s+(.+?)\s*$')
    if (!$heading.Success) { return $Fallback }
    return ($heading.Groups[1].Value -replace '[*]', '').Replace([string][char]96, '')
}

$sources = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
$pagePaths = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$pageFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$sourceFiles = @((Get-Item -LiteralPath (Join-Path $repositoryRoot 'README.md'))) + @(Get-ChildItem -LiteralPath $docsRoot -Recurse -File -Filter '*.md')
foreach ($source in $sourceFiles) {
    Assert-WikiUnlinkedPath $source.FullName
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $source.FullName).Replace('\', '/')
    $page = Get-WikiPageName $relative
    [void](Get-WikiManagedPath $outputRoot $page)
    if (!$pageFiles.Add($page) -or $page -eq '_Sidebar.md') { throw "Wiki page-name collision: $relative -> $page" }
    $content = [IO.File]::ReadAllText($source.FullName)
    $sources.Add($relative, [pscustomobject]@{ Path = $source.FullName; Content = $content; Page = $page; Title = Get-WikiTitle $content $relative })
    $pagePaths.Add($source.FullName, [IO.Path]::GetFileNameWithoutExtension($page))
}

function Convert-WikiTarget {
    param([string] $SourcePath, [string] $Target, [bool] $Image)
    $targetValue = $Target.Trim('<', '>')
    if ($targetValue.StartsWith('#') -or $targetValue -match '^(?i)(?:https?|mailto|data|codex|plugin):') { return $Target }
    $suffixMatch = [regex]::Match($targetValue, '[?#].*$')
    $suffix = if ($suffixMatch.Success) { $suffixMatch.Value } else { '' }
    $path = if ($suffixMatch.Success) { $targetValue.Substring(0, $suffixMatch.Index) } else { $targetValue }
    $lineMatch = [regex]::Match($path, ':(\d+)(?:-(\d+))?$')
    if ($lineMatch.Success) {
        $path = $path.Substring(0, $lineMatch.Index)
        if (!$suffix) {
            $suffix = '#L' + $lineMatch.Groups[1].Value
            if ($lineMatch.Groups[2].Success) { $suffix += '-L' + $lineMatch.Groups[2].Value }
        }
    }
    $decoded = [Uri]::UnescapeDataString($path)
    if (!$IsWindows -and $decoded -match '^[A-Za-z]:[/\\]') { throw "Machine-specific documentation link in $SourcePath : $Target" }
    $resolved = if ([IO.Path]::IsPathRooted($decoded)) { [IO.Path]::GetFullPath($decoded) } else { [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $SourcePath) $decoded)) }
    if (!(Test-WikiPathInside $repositoryRoot $resolved)) { throw "Documentation link leaves the source repository: $Target" }
    if (!(Test-Path -LiteralPath $resolved)) { throw "Missing documentation link in $SourcePath : $Target" }
    Assert-WikiUnlinkedPath $resolved
    if ($pagePaths.ContainsKey($resolved)) { return $pagePaths[$resolved] + $suffix }
    $container = Test-Path -LiteralPath $resolved -PathType Container
    if ($container) {
        foreach ($indexName in @('README.md', 'index.md')) {
            $indexPath = Join-Path $resolved $indexName
            if ($pagePaths.ContainsKey($indexPath)) { return $pagePaths[$indexPath] + $suffix }
        }
    }
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $resolved).Replace('\', '/')
    $encoded = ($relative.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
    $imageFile = [IO.Path]::GetExtension($resolved) -match '^(?i)\.(?:png|jpe?g|gif|svg|webp|ico)$'
    $kind = if ($container) { 'tree' } elseif ($Image -or $imageFile) { 'raw' } else { 'blob' }
    return "https://github.com/$Repository/$kind/$encodedRef/$encoded$suffix"
}

function Convert-WikiMarkdown {
    param([string] $Content, [string] $SourcePath)
    $lines = $Content.Replace("`r`n", "`n").Split([char]10)
    $fenceCharacter = ''
    $fenceLength = 0
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $fence = [regex]::Match($lines[$index], ('^\s{0,3}([' + [char]96 + '~])\1{2,}'))
        if ($fence.Success) {
            $character = $fence.Groups[1].Value
            if (!$fenceCharacter) { $fenceCharacter = $character; $fenceLength = $fence.Value.Trim().Length }
            elseif ($character -eq $fenceCharacter -and $fence.Value.Trim().Length -ge $fenceLength) { $fenceCharacter = '' }
            continue
        }
        if ($fenceCharacter) { continue }
        $lines[$index] = [regex]::Replace($lines[$index], '\]\((?<target><[^>]+>|[^\s)]+)(?:\s+(?:"[^"]*"|''[^'']*''))?\)', {
            param($match)
            $target = $match.Groups['target'].Value
            $prefix = $lines[$index].Substring(0, $match.Index)
            $image = $prefix -match '!\[[^\]]*$'
            return $match.Value.Replace($target, (Convert-WikiTarget $SourcePath $target $image))
        })
        $lines[$index] = [regex]::Replace($lines[$index], '^\s{0,3}\[[^\]]+\]:\s*(?<target><[^>]+>|\S+)', {
            param($match)
            $target = $match.Groups['target'].Value
            return $match.Value.Replace($target, (Convert-WikiTarget $SourcePath $target $false))
        })
    }
    return ($lines -join [char]10).TrimEnd() + [char]10
}

$pages = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($source in $sources.Values) {
    $pages.Add($source.Page, $script:RepoGenWikiMarker + "`n`n" + (Convert-WikiMarkdown $source.Content $source.Path))
}
$sidebar = [Collections.Generic.List[string]]::new()
$sidebar.Add($script:RepoGenWikiMarker)
$sidebar.Add('')
$sidebar.Add('# Documentation')
$sidebar.Add('')
$sidebar.Add('- [Home](Home)')
$sidebar.Add('- [Documentation](Documentation)')
foreach ($section in @('implemented', 'work-in-progress', 'future', 'other')) {
    $sectionIndex = "docs/$section/README.md"
    if (!$sources.ContainsKey($sectionIndex)) { continue }
    $sectionSource = $sources[$sectionIndex]
    $sidebar.Add("- [$($sectionSource.Title)]($([IO.Path]::GetFileNameWithoutExtension($sectionSource.Page)))")
    $children = [string[]]@($sources.Keys | Where-Object {
        (Split-Path -Parent $_).Replace('\', '/') -eq "docs/$section" -and $_ -ne $sectionIndex
    })
    if ($section -eq 'implemented') {
        $children = [string[]]@($sources.Keys | Where-Object { $_ -match '^docs/implemented/[^/]+/README\.md$' })
    }
    [Array]::Sort($children, [StringComparer]::Ordinal)
    foreach ($child in $children) {
        $childSource = $sources[$child]
        $sidebar.Add("  - [$($childSource.Title)]($([IO.Path]::GetFileNameWithoutExtension($childSource.Page)))")
    }
}
$pages.Add('_Sidebar.md', ($sidebar -join [char]10) + [char]10)
Write-WikiManagedPages $outputRoot $pages
Write-Host "Exported $($sources.Count) documentation pages and a grouped sidebar to $outputRoot"
