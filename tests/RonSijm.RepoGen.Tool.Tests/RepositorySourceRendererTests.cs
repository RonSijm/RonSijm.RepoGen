using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RepositorySourceRendererTests
{
    [Fact]
    public void GeneratesPrimaryUniqueAndCompositeIndexLookups()
    {
        using var context = new RepositoryTestDbContext(
            new DbContextOptionsBuilder<RepositoryTestDbContext>()
                .UseInMemoryDatabase(nameof(GeneratesPrimaryUniqueAndCompositeIndexLookups))
                .Options);

        var model = RepositoryModelFactory.Create(context, typeof(RepositoryTestDbContext).Assembly);
        var source = RepositorySourceRenderer.Render(model);

        source.Should().Contain("CustomerSelector ById(global::System.Guid id);");
        source.Should().Contain("CustomerSelector ByEmail(global::System.String email);");
        source.Should().Contain("CustomerCollectionSelector GetAll();");
        source.Should().Contain("CustomerCollectionSelector GetAllUnordered();");
        source.Should().Contain(
            "CustomerCollectionSelector ByRestaurantIdAndPartnerId(global::System.Guid restaurantId, global::System.Guid partnerId);");
        source.Should().Contain("void Add(global::RonSijm.RepoGen.Tool.Tests.Customer entity);");
        source.Should().Contain("Task<int> SaveChangesAsync(");
        source.Should().Contain("public abstract class BaseEntitySelector<TEntity>");
        source.Should().Contain("UpdateAsync(");
        source.Should().Contain("StageUpdateAsync(");
        source.Should().Contain("DeleteAsync(");
        source.Should().Contain("MarkForDeletionAsync(");
        source.Should().Contain("DeleteOperation Delete()");
        source.Should().Contain("SaveChangesAsync(");
        source.Should().Contain("AsNoTracking()");
        source.Should().Contain("OrderBy<TKey>(");
        source.Should().Contain("Skip(int count)");
        source.Should().Contain("MaxAsync<TResult>(");
        source.Should().Contain("SumAsync(");
        source.Should().Contain("GroupedCollectionSelector<TEntity, TKey>");
        source.Should().Contain("GroupBy<TKey>(");
        source.Should().Contain("PagedResult<T>");
        source.Should().Contain("ToPagedListAsync(");
        source.Should().Contain("FirstOrDefaultAsync(");
        source.Should().Contain("ToDictionaryAsync<TKey, TElement>(");
        source.Should().NotContain("public async global::System.Threading.Tasks.Task<int> ExecuteDeleteAsync(");
        source.Should().NotContain("public async global::System.Threading.Tasks.Task<int> ExecuteUpdateAsync(");
        source.Should().Contain("void AddRange(");
        source.Should().Contain("void UpdateRange(");
        source.Should().Contain("void RemoveRange(");
        source.Should().Contain("maxConcurrencyRetries");
        source.Should().Contain("ExecuteInTransactionAsync(");
        source.Should().NotContain("interface IDbContextResolver<out TContext>");
        source.Should().NotContain("class ScopedDbContextResolver<TContext>");
        source.Should().Contain("interface IRepositoryCacheProvider");
        source.Should().Contain("interface ICacheableCustomerRepository : ICustomerRepository");
        source.Should().Contain("class CustomerRepositoryCacheDecorator(");
        source.Should().Contain("GetOrCreateCachedAsync<TResult>(");
        source.Should().Contain("InvalidateTagAsync(CacheTag");
        source.Should().Contain("CustomerRepository(global::RonSijm.RepoGen.Tool.Tests.RepositoryTestDbContext context)");
        source.Should().NotContain("var context = ResolveContext();");
        source.Should().Contain("return new(source.SelectorContext, source.Query, InvalidateCacheAsync);");
        source.Should().Contain("internal sealed class CustomerRepository");
        source.Should().Contain("Saves every tracked change in the shared DbContext unit of work");
        source.Should().Contain("entity.RestaurantId == restaurantId && entity.PartnerId == partnerId");
    }

    [Fact]
    public void AppliesDesignerAccessorsExclusionsAndProjections()
    {
        using var context = new RepositoryTestDbContext(
            new DbContextOptionsBuilder<RepositoryTestDbContext>()
                .UseInMemoryDatabase(nameof(AppliesDesignerAccessorsExclusionsAndProjections))
                .Options);

        var design = new TestRepositoryDesigner().CreateModel();
        var model = RepositoryModelFactory.Create(
            context,
            typeof(RepositoryTestDbContext).Assembly,
            design: design);
        var repositorySource = RepositorySourceRenderer.Render(model);
        var projectionSource = ProjectionSourceRenderer.Render(model);

        repositorySource.Should().Contain("CustomerCollectionSelector ByName(global::System.String name);");
        repositorySource.Should().Contain("CustomerCollectionSelector ByNamePrefix(global::System.String prefix);");
        repositorySource.Should().Contain("entity.Name.StartsWith(prefix)");
        repositorySource.Should().Contain(
            "CustomerCollectionSelector ByRestaurantAndPartner(global::System.Guid restaurantId, global::System.Guid partnerId);");
        repositorySource.Should().Contain("entity.RestaurantId == restaurantId");
        repositorySource.Should().Contain("entity.PartnerId == partnerId");
        repositorySource.Should().Contain("Queryable.OrderBy(");
        repositorySource.Should().Contain("static entity => entity.Name");
        repositorySource.Should().Contain("OrderBy(");
        repositorySource.Should().Contain("string fieldName,");
        repositorySource.Should().Contain("StringComparison.OrdinalIgnoreCase");
        repositorySource.Should().Contain("static entity => entity.Id");
        repositorySource.Should().Contain("Sortable fields: Name, Id.");
        repositorySource.Should().Contain("static entity => entity.Restaurant");
        repositorySource.Should().NotContain("ByEmail(");
        projectionSource.Should().Contain("AsCustomerDtoAsync(");
        projectionSource.Should().Contain("AsCustomerDtosAsync(");
        projectionSource.Should().Contain(
            "static entity => new global::RonSijm.RepoGen.Tool.Tests.CustomerDto(entity.Id, entity.Name)");
        projectionSource.Should().Contain(
            "return selector.Include(static entity => entity.Restaurant).ProjectToAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.CustomerDto");
        projectionSource.Should().Contain(
            "return selector.Include(static entity => entity.Restaurant).ProjectToListAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.CustomerRestaurantDto");
        projectionSource.Should().Contain(
            "return selector.Include(static entity => entity.Visits).ProjectToListAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.CustomerVisitsDto");
        projectionSource.Should().Contain("GetCustomerPageAsync(");
        projectionSource.Should().Contain("global::RonSijm.RepoGen.PagedResult<global::RonSijm.RepoGen.Tool.Tests.CustomerDto>");
        projectionSource.Should().Contain("selector = selector.Where(entity => !(global::System.String.IsNullOrEmpty(entity.Name)));");
        projectionSource.Should().Contain("selector = selector.OrderBy(sortBy, descending);");
        projectionSource.Should().Contain("AsNoTrackingWithIdentityResolution");
        projectionSource.Should().Contain("RelationalQueryableExtensions.AsSplitQuery");
        projectionSource.Should().Contain("IgnoreQueryFilters");
        projectionSource.Should().Contain("TagWith(source, \"customer-dashboard\")");
        projectionSource.Should().Contain("RepositoryCacheEntryOptions(global::System.TimeSpan.FromTicks(");
        projectionSource.Should().Contain("GetCustomerCursorPageAsync(");
        projectionSource.Should().Contain("CursorPageRequest<global::System.Guid> cursorPage");
        projectionSource.Should().Contain("GetCustomerCountAsync(");
        projectionSource.Should().Contain("StreamCustomersAsync(");
        projectionSource.Should().Contain("GetCustomerGroupsAsync(");
        projectionSource.Should().Contain("selector.GroupBy(static entity => entity.Name).ProjectToListAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.CustomerGroupDto(entity.Key, global::System.Linq.Enumerable.Count");
        repositorySource.Should().Contain("Task<int> DeleteCustomersByRestaurantAsync(");
        repositorySource.Should().Contain("RelationalQueryableExtensions.ExecuteDeleteAsync");
        repositorySource.Should().Contain("Task<int> RenameCustomersAsync(");
        repositorySource.Should().Contain("RelationalQueryableExtensions.ExecuteUpdateAsync");
        repositorySource.Should().Contain("public async global::System.Threading.Tasks.Task<int> ExecuteDeleteAsync(");
        repositorySource.Should().Contain("public async global::System.Threading.Tasks.Task<int> ExecuteUpdateAsync(");
        repositorySource.Should().Contain(".SetProperty(static entity => entity.Name, entity => name)");
        repositorySource.Should().Contain("provider.InvalidateTagAsync(\"RonSijm.RepoGen.Tool.Tests.Customer\", cancellationToken)");
        repositorySource.Should().Contain("interface IDbContextResolver<out TContext>");
        repositorySource.Should().Contain("var context = ResolveContext();");
    }

    [Fact]
    public void AppliesGlobalGenerationSettings()
    {
        using var context = new RepositoryTestDbContext(
            new DbContextOptionsBuilder<RepositoryTestDbContext>()
                .UseInMemoryDatabase(nameof(AppliesGlobalGenerationSettings))
                .Options);

        var model = RepositoryModelFactory.Create(
            context,
            typeof(RepositoryTestDbContext).Assembly,
            design: new MinimalRepositoryDesigner().CreateModel());
        var repositorySource = RepositorySourceRenderer.Render(model);
        var projectionSource = ProjectionSourceRenderer.Render(model);

        repositorySource.Should().Contain("CustomerRepository(global::RonSijm.RepoGen.Tool.Tests.RepositoryTestDbContext context)");
        repositorySource.Should().Contain("Task AddAsync(");
        repositorySource.Should().Contain("Task<int> SaveChangesAsync(");
        repositorySource.Should().NotContain("ResolveContext()");
        repositorySource.Should().NotContain("IRepositoryCacheProvider");
        repositorySource.Should().NotContain("ICacheableCustomerRepository");
        repositorySource.Should().NotContain("void Add(");
        repositorySource.Should().NotContain("void AddRange(");
        repositorySource.Should().NotContain("AddRangeAsync(");
        repositorySource.Should().NotContain("void UpdateRange(");
        repositorySource.Should().NotContain("void RemoveRange(");
        repositorySource.Should().NotContain("maxConcurrencyRetries");
        repositorySource.Should().NotContain("ExecuteInTransactionAsync(");
        repositorySource.Should().NotContain("public bool SaveChanges()");
        repositorySource.Should().Contain("EntityFrameworkQueryableExtensions.AsNoTracking(context.Set<");
        projectionSource.Should().Contain("AsNoTracking");
        projectionSource.Should().NotContain("GetOrCreateCachedAsync");
    }

    [Fact]
    public void PreservesExplicitAccessorAndFilterMethodNames()
    {
        using var context = new RepositoryTestDbContext(
            new DbContextOptionsBuilder<RepositoryTestDbContext>()
                .UseInMemoryDatabase(nameof(PreservesExplicitAccessorAndFilterMethodNames))
                .Options);

        var model = RepositoryModelFactory.Create(
            context,
            typeof(RepositoryTestDbContext).Assembly,
            design: new ExplicitNamingRepositoryDesigner().CreateModel());
        var source = RepositorySourceRenderer.Render(model);

        source.Should().Contain("CustomerSelector ByActiveCustomerEmail(global::System.String email);");
        source.Should().NotContain("CustomerSelector ByEmail(global::System.String email);");
        source.Should().Contain("CustomerCollectionSelector ByActiveApimSubscriptionName(global::System.String name);");
    }

    [Fact]
    public void GeneratesNamedProjectionsAndCanDisableAdHocProjectionExpressions()
    {
        using var context = new RepositoryTestDbContext(
            new DbContextOptionsBuilder<RepositoryTestDbContext>()
                .UseInMemoryDatabase(nameof(GeneratesNamedProjectionsAndCanDisableAdHocProjectionExpressions))
                .Options);

        var model = RepositoryModelFactory.Create(
            context,
            typeof(RepositoryTestDbContext).Assembly,
            design: new ProjectionRestrictedRepositoryDesigner().CreateModel());
        var repositorySource = RepositorySourceRenderer.Render(model);
        var projectionSource = ProjectionSourceRenderer.Render(model);

        projectionSource.Should().Contain("ProjectToCustomerDto(");
        projectionSource.Should().Contain("ProjectToCustomName(");
        projectionSource.Should().NotContain("ProjectToCustomerDtos(");
        projectionSource.Should().Contain("GetCustomerDtosAsync(");
        projectionSource.Should().Contain("GetCustomCustomersAsync(");
        projectionSource.Should().Contain(
            "((global::RonSijm.RepoGen.BaseEntitySelector<global::RonSijm.RepoGen.Tool.Tests.Customer>)selector).ProjectToAsync");
        projectionSource.Should().Contain(
            "((global::RonSijm.RepoGen.BaseCollectionSelector<global::RonSijm.RepoGen.Tool.Tests.Customer>)selector).ProjectToListAsync");
        repositorySource.Should().Contain(
            "Ad-hoc projections are disabled for this entity. Configure the projection with HasProjection or HasQuery in the repository designer.");
        repositorySource.Should().Contain("ProjectToAsync<TProjection>(");
        repositorySource.Should().Contain("ProjectToListAsync<TProjection>(");
        repositorySource.Should().Contain("ToPagedListAsync<TProjection>(");
        repositorySource.Should().Contain("ToDictionaryAsync<TKey, TElement>(");
        repositorySource.Should().Contain("error: true");
    }

    [Fact]
    public void RejectsMissingConfiguredProjectionReferences()
    {
        var createModel = () => new MissingProjectionRepositoryDesigner().CreateModel();

        createModel.Should().Throw<InvalidOperationException>()
            .WithMessage("*GetCustomers*no matching HasProjection*");
    }

    [Fact]
    public void RejectsAmbiguousConfiguredProjectionTypeReferences()
    {
        var createModel = () => new AmbiguousProjectionRepositoryDesigner().CreateModel();

        createModel.Should().Throw<InvalidOperationException>()
            .WithMessage("*GetCustomers*2 projections match*ProjectTo(\"methodName\")*");
    }
}

internal sealed class TestRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options
            .UseDbContextResolver()
            .GenerateSetBasedOperations());

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasAccessor(customer => customer.Name);
            entity.Exclude(customer => customer.Email);
            entity.HasFilter<string>(
                "ByNamePrefix",
                (customer, prefix) => customer.Name.StartsWith(prefix));
            entity.HasFilter<Guid, Guid>(
                "ByRestaurantAndPartner",
                (customer, restaurantId, partnerId) =>
                    customer.RestaurantId == restaurantId && customer.PartnerId == partnerId);
            entity.HasDefaultSort(customer => customer.Name);
            entity.HasSortableFields(customer => customer.Name, customer => customer.Id);
            entity.Include(customer => customer.Restaurant);
            entity.ProjectTo<CustomerDto>(customer => new CustomerDto(customer.Id, customer.Name))
                .Include(customer => customer.Restaurant);
            entity.ProjectTo<CustomerRestaurantDto>(customer =>
                new CustomerRestaurantDto(customer.Id, customer.Restaurant.Id));
            entity.ProjectTo<CustomerVisitsDto>(customer =>
                new CustomerVisitsDto(customer.Id, customer.Visits.ToList()));

            var namedCustomers = entity.HasFragment(
                "NamedCustomers",
                customer => !string.IsNullOrEmpty(customer.Name));
            entity.HasQuery("GetCustomerPage")
                .Use(namedCustomers)
                .Where<string>((customer, prefix) => customer.Name.StartsWith(prefix))
                .Include(customer => customer.Restaurant)
                .OrderBy(customer => customer.Name)
                .AllowSorting(customer => customer.Name, customer => customer.Id)
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .IgnoreQueryFilters()
                .TagWith("customer-dashboard")
                .ProjectTo(customer => new CustomerDto(customer.Id, customer.Name))
                .Cache(cache => cache.For(TimeSpan.FromMinutes(5)).Sliding(TimeSpan.FromMinutes(1)).DependsOn<Visit>())
                .Paged();
            entity.HasQuery("GetCustomerCursorPage")
                .Use(namedCustomers)
                .OrderBy(customer => customer.Id)
                .ProjectTo(customer => new CustomerDto(customer.Id, customer.Name))
                .CursorPaged<Guid>((customer, cursor) => customer.Id.CompareTo(cursor) > 0, customer => customer.Id);
            entity.HasQuery("GetCustomerCount")
                .Use(namedCustomers)
                .Count();
            entity.HasQuery("StreamCustomers")
                .Use(namedCustomers)
                .ProjectTo(customer => new CustomerDto(customer.Id, customer.Name))
                .Stream();
            entity.HasQuery("GetCustomerGroups")
                .AsNoTracking()
                .GroupBy(
                    customer => customer.Name,
                    group => new CustomerGroupDto(group.Key, group.Count()));
            entity.HasCommand("DeleteCustomersByRestaurant")
                .Where<Guid>((customer, restaurantId) => customer.RestaurantId == restaurantId)
                .Delete();
            entity.HasCommand("RenameCustomers")
                .Where<Guid>((customer, restaurantId) => customer.RestaurantId == restaurantId)
                .Update(update => update.SetProperty<string, string>(
                    customer => customer.Name,
                    (customer, name) => name));
        });
    }
}

internal sealed class MinimalRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options
            .GenerateCacheDecorator(false)
            .GenerateBatchOperations(false)
            .GenerateConcurrencyRetry(false)
            .GenerateTransactions(false)
            .GenerateSynchronousMethods(false)
            .UseDefaultTracking(RepositoryTrackingBehavior.NoTracking));

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasQuery("GetCustomers")
                .ProjectTo(customer => new CustomerDto(customer.Id, customer.Name))
                .Cache()
                .List();
        });
    }
}

internal sealed class ProjectionRestrictedRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasQuery("GetCustomerDtos")
                .ProjectTo<CustomerDto>()
                .List();
            entity.HasQuery("GetCustomCustomers")
                .ProjectTo("ProjectToCustomName")
                .List();
            entity.HasProjection<CustomerDto>(customer => new CustomerDto(customer.Id, customer.Name));
            entity.HasProjection<CustomerRestaurantDto>(
                "ProjectToCustomName",
                customer => new CustomerRestaurantDto(customer.Id, customer.RestaurantId));
        });
    }
}

internal sealed class MissingProjectionRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
            entity.HasQuery("GetCustomers")
                .ProjectTo<CustomerDto>()
                .List());
    }
}

internal sealed class AmbiguousProjectionRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasProjection<CustomerDto>(customer => new CustomerDto(customer.Id, customer.Name));
            entity.HasProjection<CustomerDto>(
                "ProjectToAlternateCustomerDto",
                customer => new CustomerDto(customer.Id, customer.Email));
            entity.HasQuery("GetCustomers")
                .ProjectTo<CustomerDto>()
                .List();
        });
    }
}

internal sealed class RepositoryTestDbContext(DbContextOptions<RepositoryTestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(customer => customer.Id);
            entity.HasIndex(customer => customer.Email).IsUnique();
            entity.HasIndex(customer => new { customer.RestaurantId, customer.PartnerId });
        });
    }
}

internal sealed class Customer
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid RestaurantId { get; set; }

    public Guid PartnerId { get; set; }

    public Restaurant Restaurant { get; set; } = null!;

    public ICollection<Visit> Visits { get; set; } = [];
}

internal sealed record CustomerDto(Guid Id, string Name);

internal sealed record CustomerRestaurantDto(Guid Id, Guid RestaurantId);

internal sealed record CustomerVisitsDto(Guid Id, List<Visit> Visits);

internal sealed record CustomerGroupDto(string Name, int Count);

internal sealed class Restaurant
{
    public Guid Id { get; set; }
}

internal sealed class Visit
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
}

internal sealed class ExplicitNamingRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasAccessor(customer => customer.Email)
                .HasMethodName("ByActiveCustomerEmail")
                .IsUnique();
            entity.HasFilter<string>(
                "ByActiveApimSubscriptionName",
                (customer, name) => customer.Name == name);
        });
    }
}