param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $Run,
    [switch] $Check,
    [switch] $ValidateCompilation
)

$ErrorActionPreference = 'Stop'
$exampleRoot = $PSScriptRoot
$runnerProject = Join-Path $exampleRoot 'GenerationRunner/GenerationRunner.csproj'

function Invoke-ExampleDotNet {
    param([string[]] $CommandArguments)
    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

# One code-first runner configures all variants. It does not build generated persistence to bootstrap.
$runnerBuildArguments = @('build', $runnerProject, '--disable-build-servers', '-m:1', '-c', $Configuration, '-p:NuGetAudit=false', '--ignore-failed-sources')
if ($ValidateCompilation) {
    # Keep facade inputs out of the metadata references; validate against the examples' actual .NET 8 target.
    $inputProject = Join-Path $exampleRoot 'GenerationInputs/GenerationInputs.csproj'
    Invoke-ExampleDotNet -CommandArguments @('build', $inputProject, '--disable-build-servers', '-m:1', '-c', $Configuration, '-p:RepoGenBootstrap=true', '-p:RepoGenExportCompilationReferences=true', '-p:NuGetAudit=false', '--ignore-failed-sources')
    $compilationReferences = Join-Path $exampleRoot "GenerationInputs/bin/$Configuration/net8.0/RepoGen.CompilationReferences.txt"
    $runnerBuildArguments += '-p:RepoGenBootstrap=true'
}
Invoke-ExampleDotNet -CommandArguments $runnerBuildArguments
$runnerArguments = @('run', '--project', $runnerProject, '--no-build', '--no-restore', '-c', $Configuration, '--', $exampleRoot)
if ($Check) { $runnerArguments += '--check' }
if ($ValidateCompilation) { $runnerArguments += @('--compile-references', $compilationReferences) }
Invoke-ExampleDotNet -CommandArguments $runnerArguments

# Follow the same topic order as the example index and solution folders.
$exampleNames = @(
    'Simple', 'Configured', 'Cqrs', 'Adapters', 'AccessorAdapters',
    'Reports', 'Library', 'Membership', 'Visibility', 'Inventory', 'Collections',
    'WriteSafety', 'BatchWrites', 'Ledger', 'MultiContext', 'ScriptedPorts', 'ScriptedStreams',
    'MemoryFixtures', 'MemoryPartitions', 'MemoryCriteria', 'MemoryCoverage', 'MemoryModelFilters', 'MemoryFilteredNavigation', 'MemoryModelWrites', 'MemoryLoadingPolicies', 'MemoryJoins',
    'MemoryProxies', 'MemoryNavigation', 'MemoryProxyNavigation', 'MemoryManyToMany',
    'MemoryOptionalLinks', 'MemoryNullTargets', 'MemoryRelationshipComparers', 'MemoryLinks', 'MemorySharedLinks', 'MemoryAdditionalReferences', 'MemoryOptionalReferences', 'MemoryTranslations', 'MemoryProjectionHelpers', 'MemoryWriteTranslations', 'MemoryScalarEquality', 'MemorySharedWrites', 'MemoryBatches',
    'MemoryGeneratedValues', 'MemoryModelDefaults', 'MemoryProxyWrites', 'MemoryKeyIdentity', 'MemoryImmediateWrites',
    'MemoryAtomicWrites', 'MemoryCollections', 'CompositeCollections', 'MemorySavedResults',
    'MemoryBatchResults', 'MemoryCreateBatches', 'MemorySavedFilters', 'MemoryNativeFilters', 'RelationalContractFixture', 'RelationalWrites'
)
foreach ($exampleName in $exampleNames) {
    $exampleProject = Join-Path $exampleRoot "$exampleName/$exampleName.csproj"
    Invoke-ExampleDotNet -CommandArguments @('build', $exampleProject, '--disable-build-servers', '-m:1', '-c', $Configuration, '-p:NuGetAudit=false', '--ignore-failed-sources')
    if ($Run) { Invoke-ExampleDotNet -CommandArguments @('run', '--project', $exampleProject, '--no-build', '--no-restore', '-c', $Configuration) }
}
