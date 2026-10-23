using System.Text.Json.Nodes;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredManyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredManyGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile, true)]
    [InlineData(RepositoryOutputLayout.PerEntity, true)]
    [InlineData(RepositoryOutputLayout.PerContract, true)]
    [InlineData(RepositoryOutputLayout.SingleFile, false)]
    [InlineData(RepositoryOutputLayout.PerEntity, false)]
    [InlineData(RepositoryOutputLayout.PerContract, false)]
    public void EndpointAndJoinFiltersCompileInAllLayoutsWithoutOrmInFixtureOutput(RepositoryOutputLayout layout, bool mapped)
    {
        using var context = Context(); var design = Builder(mapped).Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, design);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("InMemoryRelationships.LoadFilteredManyToMany").And.Contain("Unfiltered = ")
            .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionalTargetNullSlotsCompileAndRetainTheirIndependentPermission(bool mapped)
    {
        using var context = Context(optional: true); var builder = Builder(mapped, optional: true);
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().Contain("preserveMissingTargets: true");
        builder.Entity<ManyReadOwner>().HasInMemoryNavigation(row => row.Tags, preserveMissingTargets: false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*optional target join foreign key*");
    }

    [Fact]
    public void ReceiptsIdentifyEachFilterAndDistinguishFilteredTargetsFromInvalidFixtures()
    {
        using var context = Context(); var bundle = RepositoryGenerator.Generate(context, Builder().Build());
        var manifest = JsonNode.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents)!;
        var json = manifest.ToJsonString();
        json.Should().Contain("OptedInExplicitClrManyToManyNoRequiredFilteredReferencesOrGraphWrites")
            .And.Contain("\"source\":true,\"target\":true,\"join\":true")
            .And.Contain("OmitLinkNotOwner").And.Contain("UnfilteredPrincipalKeysAndVisibleJoinPairsNoProviderGuarantees")
            .And.Contain("savedModelQueryFilters").And.Contain("joinRelationships");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestPermissionsNeverChangeProductionContractsOrDisabledBackendOutput(bool mapped)
    {
        using var context = Context(); var on = RepositoryGenerator.Generate(context, Builder(mapped).Build());
        var off = RepositoryGenerator.Generate(context, Builder(mapped, filters: false, strict: false).Build());
        foreach (var file in on.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            off.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        on.CreateApiSnapshot().ToJson().Should().Be(off.CreateApiSnapshot().ToJson());
        var disabled = RepositoryGenerator.Generate(context, Builder(mapped, backend: false).Build());
        RepositoryGenerator.Generate(context, Builder(mapped, filters: false, backend: false).Build()).Files.Should().BeEquivalentTo(disabled.Files);
        using var plain = ManyReadTestHarness.Context();
        RepositoryGenerator.Generate(plain, Builder(mapped).Build()).Files.Should()
            .BeEquivalentTo(RepositoryGenerator.Generate(plain, Builder(mapped, filters: false).Build()).Files);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("target")]
    [InlineData("join")]
    public void EveryFilteredDatasetRequiresGraphPermission(string participant)
    {
        using var context = Context(); var builder = Builder();
        switch (participant)
        {
            case "owner": builder.Entity<ManyReadOwner>().UseInMemoryModelQueryFilter(); break;
            case "target": builder.Entity<ManyReadTag>().UseInMemoryModelQueryFilter(); break;
            default: builder.Entity<ManyReadLink>().UseInMemoryModelQueryFilter(); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Active EF model query filters*");
    }

    [Fact]
    public void ReadPermissionsDoNotEnableFilteredSavedResultsWithoutCommandConsent()
    {
        using var context = Context(); var builder = Builder(strict: false);
        builder.Configure(options => options.RequirePortableTestCommands());
        builder.Entity<ManyReadOwner>().ExcludeCommand("Rename").HasCommand("Rename")
            .Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "changed"))
            .UseInMemoryModelQueryFilter().Save().ReturnsSaved(row => row.Tags.Count);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Active EF model query filters*");
    }
}
