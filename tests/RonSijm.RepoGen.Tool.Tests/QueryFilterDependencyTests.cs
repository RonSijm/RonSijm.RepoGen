using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class QueryFilterDependencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavigationDbSetAndTypedSetDependenciesAreDiscoveredTransitivelyAndRespectIgnoreFilters(bool replacementRoot)
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<FilterParent>(entity =>
        {
            var visible = entity.HasQuery("Visible");
            var ignored = entity.HasQuery("Ignored").IgnoreQueryFilters();
            if (replacementRoot)
            {
                visible.From(sources => sources.Set<FilterParent>().Select(row => row.Id)).List();
                ignored.From(sources => sources.Set<FilterParent>().Select(row => row.Id)).List();
            }
            else
            {
                visible.Select(row => row.Id).List();
                ignored.Select(row => row.Id).List();
            }
        });
        var model = RepositoryModelFactory.Create(context, typeof(FilterParent).Assembly, true, builder.Build());
        var queries = model.Entities.Single().Queries;
        queries.Single(query => query.MethodName == "VisibleAsync").Cache.DependentEntityTypeNames.Should()
            .Contain(RepositoryModelFactory.CSharpTypeName(typeof(FilterOwner)))
            .And.Contain(RepositoryModelFactory.CSharpTypeName(typeof(FilterRegion)))
            .And.Contain(RepositoryModelFactory.CSharpTypeName(typeof(FilterStatus)));
        queries.Single(query => query.MethodName == "IgnoredAsync").Cache.DependentEntityTypeNames.Should()
            .NotContain(RepositoryModelFactory.CSharpTypeName(typeof(FilterStatus)));
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Fact]
    public async Task WritesToAQueryFilterOnlyTableInvalidateTheCachedParentRead()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new FilterDependencyContext(new DbContextOptionsBuilder<FilterDependencyContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Set<FilterRegion>().Add(new FilterRegion { Id = 1, Active = true });
        context.Set<FilterOwner>().Add(new FilterOwner { Id = 1, RegionId = 1, Visible = true });
        context.Parents.Add(new FilterParent { Id = 1, OwnerId = 1 });
        context.Statuses.Add(new FilterStatus { Id = 1, ParentId = 1, RegionId = 1, Enabled = true });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = Builder();
        builder.Configure(options => options.UseClosedCaching("global-filter-fixture"));
        builder.Entity<FilterParent>().HasQuery("Ids").Cache().Select(row => row.Id).List();
        builder.Entity<FilterStatus>(entity =>
        {
            entity.ReadEnabled = false;
            entity.HasCommand("Change").Update(update => update.SetProperty(status => status.Enabled, (FilterStatus status, bool enabled) => enabled));
        });
        var model = RepositoryModelFactory.Create(context, typeof(FilterParent).Assembly, true, builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var parent = Activator.CreateInstance(assembly.GetType(typeof(FilterParent).Namespace + ".FilterParentRepositoryOperations")!, context, cache)!;
        var status = Activator.CreateInstance(assembly.GetType(typeof(FilterParent).Namespace + ".FilterStatusRepositoryOperations")!, context, cache)!;
        (await Call<Task<IReadOnlyList<int>>>(parent, "IdsAsync", CancellationToken.None)).Should().Equal(1);
        (await Call<Task<IReadOnlyList<int>>>(parent, "IdsAsync", CancellationToken.None)).Should().Equal(1);
        (await Call<Task<int>>(status, "ChangeAsync", false, CancellationToken.None)).Should().Be(1);
        (await Call<Task<IReadOnlyList<int>>>(parent, "IdsAsync", CancellationToken.None)).Should().BeEmpty();
    }

    private static FilterDependencyContext Context() => new(new DbContextOptionsBuilder<FilterDependencyContext>().UseSqlite("Data Source=:memory:").Options);

    private static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseClosedRepositories("Filter.Contracts"));
        builder.Entity<FilterParent>().WriteEnabled = false;
        return builder;
    }

    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;
}

public sealed class FilterParent
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public FilterOwner? Owner { get; set; }
}

public sealed class FilterOwner
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public bool Visible { get; set; }
    public FilterRegion? Region { get; set; }
}

public sealed class FilterRegion
{
    public int Id { get; set; }
    public bool Active { get; set; }
}

public sealed class FilterStatus
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public int RegionId { get; set; }
    public bool Enabled { get; set; }
}

public sealed class FilterDependencyContext(DbContextOptions<FilterDependencyContext> options) : DbContext(options)
{
    public DbSet<FilterParent> Parents => Set<FilterParent>();
    public DbSet<FilterStatus> Statuses => Set<FilterStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilterParent>().HasQueryFilter(row => row.Owner != null && row.Owner.Visible && Statuses.Any(status => status.ParentId == row.Id && status.Enabled));
        modelBuilder.Entity<FilterOwner>().HasQueryFilter(owner => owner.Region != null && owner.Region.Active);
        modelBuilder.Entity<FilterStatus>().HasQueryFilter(status => Set<FilterRegion>().Any(region => region.Id == status.RegionId && region.Active));
        modelBuilder.Entity<FilterRegion>();
    }
}