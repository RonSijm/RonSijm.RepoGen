using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AtomicFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFilterGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void FilteredAtomicBranchesCompileInEveryLayoutAndStoreShape(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = ModelWriteFilterTestHarness.Context(); var builder = Builder(mapped);
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); ImmediateTestHarness.CompileProduction(context, builder);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("updatedResultFilter: static entity => !(entity.Deleted)").And.NotContain("EntityFrameworkCore");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("UnfilteredConflictProbeAndInsertFilteredUpdateBranchOnly").And.Contain("NoInsertOrConflictFilteringNoUpdatedResultRead")
            .And.Contain("UpdateSelectionAndUpdatedModelOnlyResultReadBeforeFixtureCommit");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PermissionsLeaveProductionContractsAndApiUnchangedAndDisabledBackendsByteStable(bool mapped)
    {
        using var context = ModelWriteFilterTestHarness.Context();
        var on = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, filter: false, strict: false).Build());
        foreach (var file in on.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(on.CreateApiSnapshot().ToJson());
        RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build()).Files.Should().BeEquivalentTo(
            RepositoryGenerator.Generate(context, Builder(mapped, backend: false, filter: false).Build()).Files);
    }

    [Fact]
    public void ScalarAtomicSimulationDoesNotNeedTheAutomaticReadBackend()
    {
        using var context = ModelWriteFilterTestHarness.Context();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(queries: false).Build()));
    }

    [Fact]
    public void AbsentModelFiltersDoNotAddSourceOrReceipts()
    {
        using var context = new NativeFilterGuardContext(new DbContextOptionsBuilder<NativeFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, NativeFilterCacheKeyFactory>().Options, "plain");
        RepositoryGenerator.Generate(context, Builder().Build()).Files.Should().BeEquivalentTo(RepositoryGenerator.Generate(context, Builder(filter: false).Build()).Files);
    }
}
