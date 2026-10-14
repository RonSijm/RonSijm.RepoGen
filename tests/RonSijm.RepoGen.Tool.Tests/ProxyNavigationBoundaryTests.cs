using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyNavigationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyNavigationBoundaryTests
{
    [Theory]
    [InlineData("Comparer", "custom key conversion/comparison")]
    [InlineData("Filter", "Active EF model query filters")]
    [InlineData("AutoInclude", "AutoInclude")]
    public void ExplicitMappingsDoNotWaiveProviderRelationshipAndLoadingBoundaries(string scenario, string reason)
    {
        var options = new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options;
        using NavigationReadContext context = scenario == "Comparer" ? new NavigationComparedContext(options)
            : scenario == "Filter" ? new NavigationFilteredContext(options) : new NavigationAutoIncludeContext(options);
        var builder = Builder(strict: false); ProxyNavigationRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadLibrary>().HasQuery("Names").Select(row => row.Books.Select(book => book.Author.Name).ToList()).List();
        var coverage = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: builder.Build())
            .Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().Contain(item => item.Contains(reason, StringComparison.Ordinal));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public async Task ARootSortWhitelistDoesNotDemandUnusedMembersOnRelatedMappings()
    {
        using var context = NavigationReadTestHarness.Context();
        var builder = Builder(); ProxyNavigationRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadLibrary>().HasQuery("SortedCounts").AllowSorting(row => row.Name).Select(row => row.Books.Count).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (repository, _, _) = Repositories(assembly);
        (await NavigationReadTestHarness.Invoke<Task<IReadOnlyList<int>>>(repository, "SortedCountsAsync", "Name", false)).Should().Equal(0, 1, 2, 1);
    }

    [Fact]
    public void DeclaringAnOwnedTargetDoesNotEmitAnUnselectedReadRowDependency()
    {
        using var context = new NavigationOwnedContext(new DbContextOptionsBuilder<NavigationOwnedContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(strict: false);
        var library = builder.Entity<NavigationReadLibrary>(); library.WriteEnabled = false;
        library.HasInMemoryStore<ProxyNavigationLibraryData, int>(row => row.Key,
                row => new ProxyNavigationLibraryData(row.Key, row.Code, row.Region, row.Caption, row.Hidden))
            .MapInMemoryEntity<ProxyNavigationLibraryData>(row => new NavigationReadLibrary { Id = row.Key, Name = row.Caption })
            .HasInMemoryNavigation(row => row.Profile);
        var target = builder.Entity<NavigationReadProfile>(); target.WriteEnabled = false;
        target.HasInMemoryStore<ProxyNavigationProfileData, int>(row => row.Key,
                row => new ProxyNavigationProfileData(row.Key, row.LibraryKey, row.Caption))
            .MapInMemoryEntity<ProxyNavigationProfileData>(row => new NavigationReadProfile { Id = row.Key, LibraryId = row.LibraryKey, Caption = row.Caption });
        library.HasQuery("Names").Select(row => row.Name).List();
        var related = library.HasQuery("Related").Select(row => row.Profile == null ? "none" : row.Profile.Caption).List();

        var model = RepositoryModelFactory.Create(context, typeof(NavigationOwnedContext).Assembly, design: builder.Build());
        var queries = model.Entities.Single().Queries;
        queries.Single(query => query.MethodName == "NamesAsync").InMemoryQuery!.Supported.Should().BeTrue();
        queries.Single(query => query.MethodName == "RelatedAsync").InMemoryQuery!.Reasons.Should().Contain(reason => reason.Contains("enabled, selected ordinary", StringComparison.Ordinal));
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents))
            .Should().NotContain("__RepoGenNavigationReadProfileReadRow");
        ScriptedTestDoubleTestHarness.Compile(bundle);
        builder.Configure(options => options.RequirePortableTestQueries());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
        related.RequireTestOverride("Owned model semantics belong to a typed service-test script.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
}
