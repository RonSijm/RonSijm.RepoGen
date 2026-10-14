using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.NavigationReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationReadBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitFilterBypassDoesNotBypassAutoIncludeLoading(bool autoInclude)
    {
        var options = new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options;
        using NavigationReadContext context = autoInclude ? new NavigationAutoIncludeContext(options) : new NavigationFilteredContext(options);
        var builder = Builder();
        NavigationReadRepositoryDesigner.Stores(builder);
        var library = builder.Entity<NavigationReadLibrary>();
        library.HasQuery("Filtered").Select(row => row.Books.Select(book => book.Author.Name).ToList()).List();
        library.HasQuery("Bypassed").IgnoreQueryFilters().Select(row => row.Books.Select(book => book.Author.Name).ToList()).List();
        var queries = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: builder.Build())
            .Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries;
        queries.Single(query => query.MethodName == "FilteredAsync").InMemoryQuery!.Reasons.Should().Contain(reason => reason.Contains("Active EF model query filters", StringComparison.Ordinal));
        var bypassed = queries.Single(query => query.MethodName == "BypassedAsync").InMemoryQuery!;
        bypassed.Reasons.Should().NotContain(reason => reason.Contains("Active EF model query filters", StringComparison.Ordinal));
        bypassed.Supported.Should().Be(!autoInclude);
        if (autoInclude) bypassed.Reasons.Should().Contain(reason => reason.Contains("AutoInclude", StringComparison.Ordinal));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void ConfiguredKeyComparersAreNotSilentlyReplacedWithStructuralClrEquality()
    {
        using var context = new NavigationComparedContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder();
        NavigationReadRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadLibrary>().HasQuery("Count").Select(row => row.Books.Count).List();
        var coverage = RepositoryModelFactory.Create(context, typeof(NavigationReadContext).Assembly, design: builder.Build())
            .Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse();
        coverage.Reasons.Should().Contain(reason => reason.Contains("custom key conversion/comparison", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ReadOnly", "public readable/writable")]
    [InlineData("InitOnly", "non-init")]
    [InlineData("ShadowReference", "shadow/backing-field-only")]
    [InlineData("ConvertedReference", "custom key conversion/comparison")]
    [InlineData("CustomCollection", "detached List<T>")]
    [InlineData("Tags", "skip/many-to-many")]
    public void NonPortableRelationshipMetadataRemainsAnExplicitOverride(string navigation, string reason)
    {
        using var context = new NavigationBoundaryContext(new DbContextOptionsBuilder<NavigationBoundaryContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder();
        var owner = builder.Entity<NavigationBoundaryOwner>();
        owner.WriteEnabled = false;
        owner.HasInMemoryStore(row => row.Id, row => new NavigationBoundaryOwner { Id = row.Id, Code = row.Code });
        var target = builder.Entity<NavigationBoundaryTarget>();
        target.WriteEnabled = false;
        target.HasInMemoryStore(row => row.Id, row => new NavigationBoundaryTarget { Id = row.Id, Code = row.Code, OwnerId = row.OwnerId, Hidden = row.Hidden });
        var query = owner.HasQuery("Query").IgnoreQueryFilters();
        switch (navigation)
        {
            case "ReadOnly": owner.HasInMemoryNavigation(row => row.ReadOnly); query.Select(row => row.ReadOnly == null ? "none" : row.ReadOnly.Code).List(); break;
            case "InitOnly": owner.HasInMemoryNavigation(row => row.InitOnly); query.Select(row => row.InitOnly == null ? "none" : row.InitOnly.Code).List(); break;
            case "ShadowReference": owner.HasInMemoryNavigation(row => row.ShadowReference); query.Select(row => row.ShadowReference == null ? "none" : row.ShadowReference.Code).List(); break;
            case "ConvertedReference": owner.HasInMemoryNavigation(row => row.ConvertedReference); query.Select(row => row.ConvertedReference == null ? "none" : row.ConvertedReference.Code).List(); break;
            case "CustomCollection": owner.HasInMemoryNavigation(row => row.CustomCollection); query.Select(row => row.CustomCollection.Count).List(); break;
            case "Tags": owner.HasInMemoryNavigation(row => row.Tags); query.Select(row => row.Tags.Count).List(); break;
        }
        var coverage = RepositoryModelFactory.Create(context, typeof(NavigationBoundaryContext).Assembly, design: builder.Build())
            .Entities.Single(entity => entity.EntityName == nameof(NavigationBoundaryOwner)).Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().BeFalse();
        coverage.Reasons.Should().Contain(item => item.Contains(reason, StringComparison.Ordinal));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void RelatedGlobalFiltersRemainActiveUnlessTheOperationExplicitlyBypassesThem()
    {
        using var context = new NavigationBoundaryContext(new DbContextOptionsBuilder<NavigationBoundaryContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder();
        var owner = builder.Entity<NavigationBoundaryOwner>();
        owner.WriteEnabled = false;
        owner.HasInMemoryStore(row => row.Id, row => new NavigationBoundaryOwner { Id = row.Id });
        owner.HasInMemoryNavigation(row => row.CustomCollection);
        var target = builder.Entity<NavigationBoundaryTarget>();
        target.WriteEnabled = false;
        target.HasInMemoryStore(row => row.Id, row => new NavigationBoundaryTarget { Id = row.Id });
        owner.HasQuery("Filtered").Select(row => row.CustomCollection.Count).List();
        var model = RepositoryModelFactory.Create(context, typeof(NavigationBoundaryContext).Assembly, design: builder.Build());
        model.Entities.Single(entity => entity.EntityName == nameof(NavigationBoundaryOwner)).Queries.Single().InMemoryQuery!.Reasons
            .Should().Contain(item => item.Contains("Active EF model query filters", StringComparison.Ordinal));
    }
}
