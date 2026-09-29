using FluentAssertions;
using RonSijm.RepoGen.Design;
using Microsoft.EntityFrameworkCore;
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

        source.Should().Contain("PatientSelector ById(global::System.Guid id);");
        source.Should().Contain("PatientSelector ByEmail(global::System.String email);");
        source.Should().Contain("PatientCollectionSelector GetAll();");
        source.Should().Contain("PatientCollectionSelector GetAllUnordered();");
        source.Should().Contain(
            "PatientCollectionSelector ByPracticeIdAndPartnerId(global::System.Guid practiceId, global::System.Guid partnerId);");
        source.Should().Contain("void Add(global::RonSijm.RepoGen.Tool.Tests.Patient entity);");
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
        source.Should().Contain("interface ICacheablePatientRepository : IPatientRepository");
        source.Should().Contain("class PatientRepositoryCacheDecorator(");
        source.Should().Contain("GetOrCreateCachedAsync<TResult>(");
        source.Should().Contain("InvalidateTagAsync(CacheTag");
        source.Should().Contain("PatientRepository(global::RonSijm.RepoGen.Tool.Tests.RepositoryTestDbContext context)");
        source.Should().NotContain("var context = ResolveContext();");
        source.Should().Contain("return new(source.SelectorContext, source.Query, InvalidateCacheAsync);");
        source.Should().Contain("internal sealed class PatientRepository");
        source.Should().Contain("Saves every tracked change in the shared DbContext unit of work");
        source.Should().Contain("entity.PracticeId == practiceId && entity.PartnerId == partnerId");
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

        repositorySource.Should().Contain("PatientCollectionSelector ByName(global::System.String name);");
        repositorySource.Should().Contain("PatientCollectionSelector ByNamePrefix(global::System.String prefix);");
        repositorySource.Should().Contain("entity.Name.StartsWith(prefix)");
        repositorySource.Should().Contain(
            "PatientCollectionSelector ByPracticeAndPartner(global::System.Guid practiceId, global::System.Guid partnerId);");
        repositorySource.Should().Contain("entity.PracticeId == practiceId");
        repositorySource.Should().Contain("entity.PartnerId == partnerId");
        repositorySource.Should().Contain("Queryable.OrderBy(");
        repositorySource.Should().Contain("static entity => entity.Name");
        repositorySource.Should().Contain("OrderBy(");
        repositorySource.Should().Contain("string fieldName,");
        repositorySource.Should().Contain("StringComparison.OrdinalIgnoreCase");
        repositorySource.Should().Contain("static entity => entity.Id");
        repositorySource.Should().Contain("Sortable fields: Name, Id.");
        repositorySource.Should().Contain("static entity => entity.Practice");
        repositorySource.Should().NotContain("ByEmail(");
        projectionSource.Should().Contain("AsPatientDtoAsync(");
        projectionSource.Should().Contain("AsPatientDtosAsync(");
        projectionSource.Should().Contain(
            "static entity => new global::RonSijm.RepoGen.Tool.Tests.PatientDto(entity.Id, entity.Name)");
        projectionSource.Should().Contain(
            "return selector.Include(static entity => entity.Practice).ProjectToAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.PatientDto");
        projectionSource.Should().Contain(
            "return selector.Include(static entity => entity.Practice).ProjectToListAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.PatientPracticeDto");
        projectionSource.Should().Contain(
            "return selector.Include(static entity => entity.Visits).ProjectToListAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.PatientVisitsDto");
        projectionSource.Should().Contain("GetPatientPageAsync(");
        projectionSource.Should().Contain("global::RonSijm.RepoGen.PagedResult<global::RonSijm.RepoGen.Tool.Tests.PatientDto>");
        projectionSource.Should().Contain("selector = selector.Where(entity => !(global::System.String.IsNullOrEmpty(entity.Name)));");
        projectionSource.Should().Contain("selector = selector.OrderBy(sortBy, descending);");
        projectionSource.Should().Contain("AsNoTrackingWithIdentityResolution");
        projectionSource.Should().Contain("RelationalQueryableExtensions.AsSplitQuery");
        projectionSource.Should().Contain("IgnoreQueryFilters");
        projectionSource.Should().Contain("TagWith(source, \"patient-dashboard\")");
        projectionSource.Should().Contain("RepositoryCacheEntryOptions(global::System.TimeSpan.FromTicks(");
        projectionSource.Should().Contain("GetPatientCursorPageAsync(");
        projectionSource.Should().Contain("CursorPageRequest<global::System.Guid> cursorPage");
        projectionSource.Should().Contain("GetPatientCountAsync(");
        projectionSource.Should().Contain("StreamPatientsAsync(");
        projectionSource.Should().Contain("GetPatientGroupsAsync(");
        projectionSource.Should().Contain("selector.GroupBy(static entity => entity.Name).ProjectToListAsync(static entity => new global::RonSijm.RepoGen.Tool.Tests.PatientGroupDto(entity.Key, global::System.Linq.Enumerable.Count");
        repositorySource.Should().Contain("Task<int> DeletePatientsByPracticeAsync(");
        repositorySource.Should().Contain("RelationalQueryableExtensions.ExecuteDeleteAsync");
        repositorySource.Should().Contain("Task<int> RenamePatientsAsync(");
        repositorySource.Should().Contain("RelationalQueryableExtensions.ExecuteUpdateAsync");
        repositorySource.Should().Contain("public async global::System.Threading.Tasks.Task<int> ExecuteDeleteAsync(");
        repositorySource.Should().Contain("public async global::System.Threading.Tasks.Task<int> ExecuteUpdateAsync(");
        repositorySource.Should().Contain(".SetProperty(static entity => entity.Name, entity => name)");
        repositorySource.Should().Contain("provider.InvalidateTagAsync(\"RonSijm.RepoGen.Tool.Tests.Patient\", cancellationToken)");
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

        repositorySource.Should().Contain("PatientRepository(global::RonSijm.RepoGen.Tool.Tests.RepositoryTestDbContext context)");
        repositorySource.Should().Contain("Task AddAsync(");
        repositorySource.Should().Contain("Task<int> SaveChangesAsync(");
        repositorySource.Should().NotContain("ResolveContext()");
        repositorySource.Should().NotContain("IRepositoryCacheProvider");
        repositorySource.Should().NotContain("ICacheablePatientRepository");
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

        source.Should().Contain("PatientSelector ByActivePatientEmail(global::System.String email);");
        source.Should().NotContain("PatientSelector ByEmail(global::System.String email);");
        source.Should().Contain("PatientCollectionSelector ByActiveApimSubscriptionName(global::System.String name);");
    }
}

internal sealed class TestRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options
            .UseDbContextResolver()
            .GenerateSetBasedOperations());

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasAccessor(patient => patient.Name);
            entity.Exclude(patient => patient.Email);
            entity.HasFilter<string>(
                "ByNamePrefix",
                (patient, prefix) => patient.Name.StartsWith(prefix));
            entity.HasFilter<Guid, Guid>(
                "ByPracticeAndPartner",
                (patient, practiceId, partnerId) =>
                    patient.PracticeId == practiceId && patient.PartnerId == partnerId);
            entity.HasDefaultSort(patient => patient.Name);
            entity.HasSortableFields(patient => patient.Name, patient => patient.Id);
            entity.Include(patient => patient.Practice);
            entity.ProjectTo<PatientDto>(patient => new PatientDto(patient.Id, patient.Name))
                .Include(patient => patient.Practice);
            entity.ProjectTo<PatientPracticeDto>(patient =>
                new PatientPracticeDto(patient.Id, patient.Practice.Id));
            entity.ProjectTo<PatientVisitsDto>(patient =>
                new PatientVisitsDto(patient.Id, patient.Visits.ToList()));

            var namedPatients = entity.HasFragment(
                "NamedPatients",
                patient => !string.IsNullOrEmpty(patient.Name));
            entity.HasQuery("GetPatientPage")
                .Use(namedPatients)
                .Where<string>((patient, prefix) => patient.Name.StartsWith(prefix))
                .Include(patient => patient.Practice)
                .OrderBy(patient => patient.Name)
                .AllowSorting(patient => patient.Name, patient => patient.Id)
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .IgnoreQueryFilters()
                .TagWith("patient-dashboard")
                .ProjectTo(patient => new PatientDto(patient.Id, patient.Name))
                .Cache(cache => cache.For(TimeSpan.FromMinutes(5)).Sliding(TimeSpan.FromMinutes(1)).DependsOn<Visit>())
                .Paged();
            entity.HasQuery("GetPatientCursorPage")
                .Use(namedPatients)
                .OrderBy(patient => patient.Id)
                .ProjectTo(patient => new PatientDto(patient.Id, patient.Name))
                .CursorPaged<Guid>((patient, cursor) => patient.Id.CompareTo(cursor) > 0, patient => patient.Id);
            entity.HasQuery("GetPatientCount")
                .Use(namedPatients)
                .Count();
            entity.HasQuery("StreamPatients")
                .Use(namedPatients)
                .ProjectTo(patient => new PatientDto(patient.Id, patient.Name))
                .Stream();
            entity.HasQuery("GetPatientGroups")
                .AsNoTracking()
                .GroupBy(
                    patient => patient.Name,
                    group => new PatientGroupDto(group.Key, group.Count()));
            entity.HasCommand("DeletePatientsByPractice")
                .Where<Guid>((patient, practiceId) => patient.PracticeId == practiceId)
                .Delete();
            entity.HasCommand("RenamePatients")
                .Where<Guid>((patient, practiceId) => patient.PracticeId == practiceId)
                .Update(update => update.SetProperty<string, string>(
                    patient => patient.Name,
                    (patient, name) => name));
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

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasQuery("GetPatients")
                .ProjectTo(patient => new PatientDto(patient.Id, patient.Name))
                .Cache()
                .List();
        });
    }
}

internal sealed class RepositoryTestDbContext(DbContextOptions<RepositoryTestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(patient => patient.Id);
            entity.HasIndex(patient => patient.Email).IsUnique();
            entity.HasIndex(patient => new { patient.PracticeId, patient.PartnerId });
        });
    }
}

internal sealed class Patient
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid PracticeId { get; set; }

    public Guid PartnerId { get; set; }

    public Practice Practice { get; set; } = null!;

    public ICollection<Visit> Visits { get; set; } = [];
}

internal sealed record PatientDto(Guid Id, string Name);

internal sealed record PatientPracticeDto(Guid Id, Guid PracticeId);

internal sealed record PatientVisitsDto(Guid Id, List<Visit> Visits);

internal sealed record PatientGroupDto(string Name, int Count);

internal sealed class Practice
{
    public Guid Id { get; set; }
}

internal sealed class Visit
{
    public Guid Id { get; set; }

    public Guid PatientId { get; set; }
}

internal sealed class ExplicitNamingRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasAccessor(patient => patient.Email)
                .HasMethodName("ByActivePatientEmail")
                .IsUnique();
            entity.HasFilter<string>(
                "ByActiveApimSubscriptionName",
                (patient, name) => patient.Name == name);
        });
    }
}