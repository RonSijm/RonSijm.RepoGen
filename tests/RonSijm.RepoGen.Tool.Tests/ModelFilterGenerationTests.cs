using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ModelFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void ScalarModelFiltersCompileInAllLayoutsAndStoreShapes(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain(".Deleted").And.Contain(".Archived").And.NotContain("EntityFrameworkCore");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("UseInMemoryModelQueryFilter").And.Contain("BeforeDesignerVisibilityAndQueryPipeline")
            .And.Contain("IgnoreQueryFiltersOnlyNotBypassVisibility").And.Contain("RowOnlyReadRootsNoFilteredNavigationGraphsOrWrites");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProductionContractsAndApiAreUnchangedByFixtureFilterOptIn(bool mapped)
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, enabled: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        off.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
        off.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("modelQueryFilters");
    }

    [Fact]
    public void DisabledBackendAndAbsentFiltersRemainByteIdenticalAndSettingCanBeRemoved()
    {
        using var context = Context(); var builder = Builder(queries: false);
        var baseline = RepositoryGenerator.Generate(context, builder.Build());
        builder.Entity<ModelFilterRow>().UseInMemoryModelQueryFilter(false);
        builder.Entity<ModelFilterFolder>().UseInMemoryModelQueryFilter(false);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().BeEquivalentTo(baseline.Files);
        using var plain = MemoryWriteTestHarness.Context(); var simple = ProjectionTranslationTestHarness.Builder(commands: false);
        var before = RepositoryGenerator.Generate(plain, simple.Build());
        simple.Entity<MemoryWriteNote>().UseInMemoryModelQueryFilter();
        RepositoryGenerator.Generate(plain, simple.Build()).Files.Should().BeEquivalentTo(before.Files);
    }

    [Fact]
    public void EveryFilteredSourceNeedsItsOwnOptInRatherThanInheritingTheOwnerPolicy()
    {
        using var context = Context(); var builder = Builder(strict: false);
        builder.Entity<ModelFilterFolder>().UseInMemoryModelQueryFilter(false);
        var entity = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities.Single(item => item.EntityName == "ModelFilterRow");
        entity.Queries.Single(query => query.MethodName == "CardsAsync").InMemoryQuery!.Supported.Should().BeTrue();
        entity.Queries.Single(query => query.MethodName == "JoinedAsync").InMemoryQuery!.Supported.Should().BeFalse();
        entity.Queries.Single(query => query.MethodName == "JoinedAllAsync").InMemoryQuery!.Supported.Should().BeTrue();
        builder.Configure(options => options.RequirePortableTestQueries());
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
    }

    [Fact]
    public void IgnoreQueryFiltersLeavesNoReceiptAndDoesNotNeedTheOptIn()
    {
        using var context = Context(); var builder = Base(enabled: false);
        builder.Entity<ModelFilterRow>().HasQuery("Unfiltered").IgnoreQueryFilters().Select(row => row.Id).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        ScriptedTestDoubleTestHarness.Compile(bundle);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().NotContain("modelQueryFilters");
    }
}
