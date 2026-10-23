using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.AutoIncludeReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AutoIncludeGuardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnconfiguredModelLoadingStillRequiresAnExplicitOverride(bool mapped)
    {
        using var context = Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(mapped, ignore: false).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*AutoInclude*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoringAutomaticLoadingDoesNotIgnoreRelatedModelFilters(bool ignoreFilters)
    {
        using var context = new NavigationAutoIncludeContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = NavigationReadTestHarness.Builder(); NavigationReadRepositoryDesigner.Stores(builder);
        builder.Entity<NavigationReadLibrary>().HasQuery("Names").IgnoreAutoIncludes().IgnoreQueryFilters(ignoreFilters)
            .Select(row => row.Books.Select(book => book.Author.Name).ToList()).List();
        var coverage = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build())
            .Entities.Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries.Single().InMemoryQuery!;
        coverage.Supported.Should().Be(ignoreFilters);
        coverage.Reasons.Should().NotContain(reason => reason.Contains("AutoInclude", StringComparison.Ordinal));
        if (!ignoreFilters) coverage.Reasons.Should().Contain(reason => reason.Contains("Active EF model query filters", StringComparison.Ordinal));
    }

    [Fact]
    public void OwnedConventionLoadingCannotBeAcknowledgedAwayByTheOption()
    {
        using var context = new AutoIncludeOwnedContext(new DbContextOptionsBuilder<AutoIncludeOwnedContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Owned.Contracts").UseScriptedTestDoubles("Owned.Testing").UseInMemoryTestStores().UseInMemoryTestQueries());
        var entity = builder.Entity<NavigationReadLibrary>(); entity.WriteEnabled = false;
        entity.HasInMemoryStore(row => row.Id, row => new NavigationReadLibrary { Id = row.Id });
        entity.HasQuery("Ids").IgnoreAutoIncludes().IgnoreQueryFilters().Select(row => row.Id).List();
        var query = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities.Single().Queries.Single();
        query.IgnoreAutoIncludes.Should().BeTrue(); query.InMemoryQuery!.Supported.Should().BeFalse();
        query.InMemoryQuery.Reasons.Should().Contain(reason => reason.Contains("AutoInclude", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegisteredProvidersOwnTheirLoadingPolicyRegardlessOfBuilderOrder(bool beforeRoot)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseClosedRepositories("Registered.Contracts").GenerateOnlyConfiguredEntities());
        var query = builder.Entity<CommandRow>().HasQuery("Names");
        if (beforeRoot) query.IgnoreAutoIncludes();
        var result = query.FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request);
        if (!beforeRoot) result.IgnoreAutoIncludes();
        result.List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1304:*owns its root policies*");
    }

    [Fact]
    public void SupersededResultBuilderCannotMutateTheNamedRootPolicy()
    {
        var builder = new RepositoryModelBuilder();
        var original = builder.Entity<NavigationReadLibrary>().HasQuery("Names").Select(row => row.Id);
        original.Select(id => (long)id).List();
        Action mutate = () => original.IgnoreAutoIncludes();
        mutate.Should().Throw<InvalidOperationException>();
        builder.Build().Entities.Single().Queries.Single().IgnoreAutoIncludes.Should().BeFalse();
    }

    [Fact]
    public void ExplicitFilteredLoadingRemainsUnsupportedByFixtures()
    {
        using var context = Context(); var builder = Builder(strict: false);
        builder.Entity<NavigationReadLibrary>().HasQuery("FilteredLoad").IgnoreAutoIncludes()
            .Load(load => load.Include(row => row.Books.Where(book => !book.Archived)))
            .Select(row => row.Books.Count).List();
        var query = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities
            .Single(entity => entity.EntityName == nameof(NavigationReadLibrary)).Queries.Single(query => query.MethodName == "FilteredLoadAsync");
        query.InMemoryQuery!.Supported.Should().BeFalse();
        query.InMemoryQuery.Reasons.Should().Contain(reason => reason.Contains("Filtered Include", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitPortableRootFilterAndLoadingSuppressionRemainIndependent()
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, ModelFilterCacheKeyFactory>().Options, "autoinclude");
        var builder = ModelFilterTestHarness.Base();
        builder.Entity<ModelFilterRow>().HasQuery("Cards").IgnoreAutoIncludes().Select(row => new ModelFilterCard(row.Id, row.Title)).List();
        var query = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities
            .Single(entity => entity.EntityName == nameof(ModelFilterRow)).Queries.Single();
        query.InMemoryQuery!.Supported.Should().BeTrue(); query.InMemoryQuery.ModelQueryFilters.Should().ContainSingle();
        builder.Entity<ModelFilterRow>().UseInMemoryModelQueryFilter(false);
        builder.Configure(options => options.RequirePortableTestQueries(false));
        query = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities
            .Single(entity => entity.EntityName == nameof(ModelFilterRow)).Queries.Single();
        query.InMemoryQuery!.Supported.Should().BeFalse();
        query.InMemoryQuery.Reasons.Should().Contain(reason => reason.Contains("Active EF model query filters", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitSkipNavigationReadsCanSuppressModelLoadingWithoutEnablingWrites(bool mapped)
    {
        using var context = new ManyReadAutoIncludeContext(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = ManyReadTestHarness.Builder(mapped, strict: false);
        builder.Entity<ManyReadOwner>().HasQuery("ExplicitTags").IgnoreAutoIncludes().Select(row => new ManyReadCard(row.Id, row.Name,
            row.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList())).List();
        var entity = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build()).Entities.Single(entity => entity.EntityName == nameof(ManyReadOwner));
        entity.Queries.Single(query => query.MethodName == "ExplicitTagsAsync").InMemoryQuery!.Supported.Should().BeTrue();
        entity.Commands.Should().OnlyContain(command => !command.InMemoryCommand!.Supported);
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
}
