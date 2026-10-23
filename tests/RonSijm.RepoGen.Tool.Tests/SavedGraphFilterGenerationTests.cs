using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.SavedGraphFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedGraphFilterGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void SavedGraphFiltersCompileInEveryLayoutAndBothStoreShapes(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = ModelFilterTestHarness.Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("savedModelQueryFilters").And.Contain("AfterSharedSaveBeforeNavigationLoadingAndResultVisibility")
            .And.Contain("PostSaveFailureNeverSilentlyShortenBatches").And.Contain("CommandOwnedNotInheritedFromEntityReads");
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("EntityFrameworkCore");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OffAndDisabledBackendRetainProductionContractsAndApiAndFlagsCanBeReset(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, graphFilters: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        off.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("savedModelQueryFilters");
        RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build()).Files.Should().BeEquivalentTo(
            RepositoryGenerator.Generate(context, Builder(mapped, graphFilters: false, backend: false).Build()).Files);
        var builder = Base(mapped); var command = builder.Entity<ModelFilterRow>().HasCommand("Stage").Where(item => item.Id == 1)
            .Patch(setters => setters.SetProperty(item => item.Title, "changed")).Stage().UseInMemoryModelQueryFilter(includeNavigations: true);
        command.UseInMemoryModelQueryFilter(); builder.Build().Entities.Single(item => item.EntityType == typeof(ModelFilterRow))
            .Commands.Single().InMemoryModelQueryFilterNavigationsEnabled.Should().BeFalse();
        command.UseInMemoryModelQueryFilter(false, includeNavigations: true);
        builder.Build().Entities.Single(item => item.EntityType == typeof(ModelFilterRow)).Commands.Single()
            .InMemoryModelQueryFilterNavigationsEnabled.Should().BeFalse();
    }
}
