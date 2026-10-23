using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredCollectionGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void FilteredReconciliationCompilesInEveryLayoutWithoutEfInFixtureOutput(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var builder = Builder(mapped);
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, builder.Build());
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("child.Text != \"deleted\"").And.Contain("entity.OwnerId > 0")
            .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestOnlyPermissionsDoNotChangeProductionContractsApiOrDisabledOutput(bool mapped)
    {
        using var context = Context(); var on = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, filters: false, strict: false).Build());
        foreach (var file in on.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        on.CreateApiSnapshot().ToJson().Should().Be(off.CreateApiSnapshot().ToJson());
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build());
        RepositoryGenerator.Generate(context, Builder(mapped, filters: false, backend: false).Build()).Files.Should().BeEquivalentTo(disabled.Files);
        using var plain = MemoryCollectionTestHarness.Context();
        RepositoryGenerator.Generate(plain, Builder(mapped).Build()).Files.Should()
            .BeEquivalentTo(RepositoryGenerator.Generate(plain, Builder(mapped, filters: false).Build()).Files);
    }

    [Fact]
    public void ReceiptsDescribeIndependentSelectionInsertionDirtyTargetsAndSavedResults()
    {
        using var context = Context(); var manifest = RepositoryGenerator.Generate(context, Builder().Build())
            .Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("SimulateCollectionWrites(includeModelQueryFilters: true)")
            .And.Contain("OwnerScopeThenModelFilterThenDesignerVisibilityBeforeBoundsMatchingAndMissingItems")
            .And.Contain("NotValidatedAgainstQueryFiltersAllSavedKeysStillReserveIdentity")
            .And.Contain("AllOwnerScopedRowsIncludingFilteredRows").And.Contain("IndependentReturnsSavedGraphPermission");
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("parent")]
    [InlineData("simulation")]
    public void ReadPermissionAndSimulationNeverImplicitlyGrantFilteredWritePermission(string missing)
    {
        using var context = Context();
        var builder = Builder(filters: missing != "collection", root: missing != "parent", simulate: missing != "simulation");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
}
