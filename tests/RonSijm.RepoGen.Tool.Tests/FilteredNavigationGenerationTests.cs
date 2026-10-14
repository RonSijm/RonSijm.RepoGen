using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredNavigationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredNavigationGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void ExplicitFilteredGraphsCompileWithoutEfInTheFixture(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = ModelFilterTestHarness.Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents))
            .Should().NotContain("EntityFrameworkCore");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("BeforeDeclaredNavigationLoadingThenDesignerVisibilityAndQueryPipeline")
            .And.Contain("OptedInOrdinaryCollectionsAndOptionalReferencesNoRequiredFilteredReferencesManyToManyOrWrites");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContractsProductionAndApiRemainUnchangedAndDisabledBackendIsByteStable(bool mapped)
    {
        using var context = ModelFilterTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, navigationFilters: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        RepositoryGenerator.Generate(context, Builder(mapped, queries: false).Build()).Files.Should().BeEquivalentTo(
            RepositoryGenerator.Generate(context, Builder(mapped, navigationFilters: false, queries: false).Build()).Files);
        var builder = Base(mapped); var entity = builder.Entity<ModelFilterRow>();
        entity.UseInMemoryModelQueryFilter(false, includeNavigations: true);
        builder.Build().Entities.Single(item => item.EntityType == typeof(ModelFilterRow)).InMemoryModelQueryFilterNavigationsEnabled.Should().BeFalse();
    }
}
