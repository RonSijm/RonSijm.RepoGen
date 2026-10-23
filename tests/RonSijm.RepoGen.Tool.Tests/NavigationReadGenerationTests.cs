using System.Text.Json;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationReadGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryLayoutCompilesIndependentTypedDetachedLoading(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new NavigationReadRepositoryDesigner(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("InMemoryRelationships.LoadCollection").And.Contain("InMemoryRelationships.LoadReference")
            .And.Contain("entity.LibraryCode, entity.LibraryRegion").And.Contain("entity.Code, entity.Region")
            .And.NotContain("Microsoft.EntityFrameworkCore").And.NotContain("System.Linq.Queryable");
    }

    [Fact]
    public void DisablingNavigationOrBackendNeverChangesProductionApiOrQueryFingerprints()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, new NavigationReadRepositoryDesigner());
        var model = Model(context, new NavigationReadRepositoryDesigner());
        foreach (var designer in new[] { new NavigationReadRepositoryDesigner(navigations: false, strict: false), new NavigationReadRepositoryDesigner(enabled: false) })
        {
            var bundle = RepositoryGenerator.Generate(context, designer);
            enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
                .Should().Equal(bundle.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
            enabled.CreateApiSnapshot().ToJson().Should().Be(bundle.CreateApiSnapshot().ToJson());
            JsonSerializer.Serialize(model.Entities.SelectMany(entity => entity.Queries)).Should().Be(JsonSerializer.Serialize(Model(context, designer).Entities.SelectMany(entity => entity.Queries)));
            string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("InMemoryRelationships.Load");
        }
    }

    [Fact]
    public void ReceiptsRecordOnlyParticipatingStoresAndExplicitKeysWithoutPolicyInjection()
    {
        using var context = Context();
        var model = Model(context, new NavigationReadRepositoryDesigner());
        var library = model.Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary));
        var detail = library.Queries.Single(query => query.MethodName == "DetailAsync").InMemoryQuery!;
        detail.Supported.Should().BeTrue();
        detail.Navigation!.Roots.Select(root => root.EntityTypeName).Should().HaveCount(3).And.NotContain(RepositoryModelFactory.CSharpTypeName(typeof(NavigationReadProfile)));
        detail.Navigation.Relationships.Select(edge => edge.Name).Should().HaveCount(2);
        detail.Navigation.Relationships.Single(edge => edge.Name.EndsWith(".Author", StringComparison.Ordinal)).Required.Should().BeTrue();
        var manifest = RepositoryGenerator.Generate(context, new NavigationReadRepositoryDesigner()).Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("navigationSnapshot").And.Contain("ExplicitMappedKeyRelationshipsOnly").And.Contain("NoImplicitRepoGenPolicyInjection");
    }

    [Fact]
    public void DeclarationsAreDirectActualMappedMembersAndCanBeRemovedIdempotently()
    {
        var builder = Builder();
        var library = builder.Entity<NavigationReadLibrary>();
        Action nested = () => library.HasInMemoryNavigation(row => row.Books.First().Author);
        nested.Should().Throw<InvalidOperationException>().WithMessage("RGD3304:*direct mapped navigation*");
        Action missing = () => library.HasInMemoryNavigation<string>(null!);
        missing.Should().Throw<ArgumentNullException>();
        library.HasInMemoryNavigation(row => row.Books).HasInMemoryNavigation(row => row.Books);
        builder.Build().Entities.Single().InMemoryNavigations.Should().HaveCount(1);
        library.WithoutInMemoryNavigation(row => row.Books).WithoutInMemoryNavigation(row => row.Books);
        builder.Build().Entities.Single().InMemoryNavigations.Should().BeEmpty();
        library.HasInMemoryNavigation(row => row.Name);
        using var context = Context();
        Action invalid = () => RepositoryGenerator.Generate(context, builder.Build());
        invalid.Should().Throw<InvalidOperationException>().WithMessage("RGD3304:*actual mapped navigation*");
        builder.Configure(options => options.UseInMemoryTestQueries(false));
        library.HasInMemoryStore(row => row.Id, row => new NavigationReadLibrary { Id = row.Id });
        RepositoryGenerator.Generate(context, builder.Build()); // Inactive test-read declarations do not affect production.
    }

    [Fact]
    public void MissingRelationshipDeclarationsRequireExplicitAcknowledgementInStrictMode()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        NavigationReadRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadBook>().WithoutInMemoryNavigation(row => row.Author);
        var query = builder.Entity<NavigationReadLibrary>().HasQuery("Authors").Select(row => row.Books.Select(book => book.Author.Name).ToList());
        query.List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Author*HasInMemoryNavigation*");
        query.RequireTestOverride("Application arranges a custom author graph.");
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("RequiresOverride");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Theory]
    [InlineData("Excluded")]
    [InlineData("MissingStore")]
    [InlineData("Proxy")]
    [InlineData("FilteredInclude")]
    public void UnsupportedLoadingNeverSilentlyUsesASeededGraph(string scenario)
    {
        using var context = Context();
        var builder = Builder();
        NavigationReadRepositoryDesigner.Stores(builder);
        var library = builder.Entity<NavigationReadLibrary>();
        switch (scenario)
        {
            case "Excluded": builder.Entity<NavigationReadBook>().GenerateTestDouble(false); break;
            case "MissingStore": builder.Entity<NavigationReadBook>().WithoutInMemoryStore(); break;
            case "Proxy": builder.Entity<NavigationReadBook>().HasInMemoryStore<ProxyReadData, int>(row => row.Key, row => new ProxyReadData { Key = row.Key }); break;
            case "FilteredInclude": library.Include(row => row.Books.Where(book => book.Price > 10)); break;
        }
        library.HasQuery("Details").Select(row => row.Books.Count).List();
        var coverage = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: builder.Build())
            .Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse();
        coverage.Reasons.Should().NotBeEmpty();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void RootAndRelatedPoliciesRemainDifferentAndReviewIsStillRequiredWhenConfigured()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        builder.Configure(options => options.RequireNavigationVisibilityReview());
        NavigationReadRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadBook>().HasVisibilityPolicy("Books", row => !row.Archived);
        var library = builder.Entity<NavigationReadLibrary>();
        library.HasQuery("Counts").Select(row => row.Books.Count).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD*Books*ReviewNavigationVisibility*");
        library.ReviewNavigationVisibility<NavigationReadBook>("Books", "This report deliberately counts archived books too.");
        RepositoryGenerator.Generate(context, builder.Build());
    }

    private static RepositoryGenerationModel Model(NavigationReadContext context, RepositoryDesigner designer) =>
        RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: designer.CreateModel());
}
