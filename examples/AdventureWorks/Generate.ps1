param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Check,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'

function Invoke-DotNet([string[]]$CommandArguments) {
    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

$checkArguments = @()
if ($Check) {
    $checkArguments = @('--check')
}

# Both generation stages are independent of the generated consumer.
$scaffolding = Join-Path $PSScriptRoot 'Scaffolding/AdventureWorks.Scaffolding.csproj'
Invoke-DotNet @('build', $scaffolding, '-c', $Configuration, '-m:1', '--verbosity', 'minimal', '-p:UseSharedCompilation=false')
$scaffolder = Join-Path $PSScriptRoot "Scaffolding/bin/$Configuration/net10.0/AdventureWorks.Scaffolding.dll"
Invoke-DotNet (@($scaffolder, $PSScriptRoot) + $checkArguments)

$generation = Join-Path $PSScriptRoot 'Generation/AdventureWorks.Generation.csproj'
Invoke-DotNet @('build', $generation, '-c', $Configuration, '-m:1', '--verbosity', 'minimal', '-p:UseSharedCompilation=false')
$generator = Join-Path $PSScriptRoot "Generation/bin/$Configuration/net10.0/AdventureWorks.Generation.dll"
Invoke-DotNet (@($generator, $PSScriptRoot) + $checkArguments)

$demo = Join-Path $PSScriptRoot 'Demo/AdventureWorks.Demo.csproj'
Invoke-DotNet @('build', $demo, '-c', $Configuration, '-m:1', '--verbosity', 'minimal', '-p:UseSharedCompilation=false')
if ($Run) {
    $executable = Join-Path $PSScriptRoot "Demo/bin/$Configuration/net8.0/AdventureWorks.Demo.dll"
    Invoke-DotNet @($executable)
}
