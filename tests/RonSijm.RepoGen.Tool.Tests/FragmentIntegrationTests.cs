using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FragmentIntegrationTests
{
    [Fact]
    public async Task KeyValidationCompositionAndWriteReadsShareOneRequestAndOneFixedKeyWork()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.Add(new CommandRow { Id = 1, Name = "Ada", OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Fragments.Keyed")
            .UseKeyedContextLeases<string>().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CommandRow>();
        var owner = entity.HasFragment<KeyedRequest>("Owner", (row, ownerInput) => row.OwnerId == ownerInput.OwnerId);
        var name = entity.HasFragment<KeyedRequest>("Name", (other, search) => other.Name == search.Name);
        var active = entity.HasFragment("PositiveRevision", row => row.Revision >= 0);
        entity.HasQuery("Read").UseContextKey<KeyedRequest, string>(request => request.Database)
            .Validate<KeyedRequest>(request => request.OwnerId > 0, "An owner is required.")
            .Use(owner.And(active).WithRequestName("request")).UseIf<KeyedRequest>(request => request.Name.Length > 0, name)
            .Select(row => row.Name).List();
        entity.HasQuery("LoadForUpdate").OnWriteRepository().UseContextKey<KeyedRequest, string>(request => request.Database)
            .Use(owner.And(name).WithRequestName("request")).ProjectTo(row => row.Name).SingleOrDefault();
        var model = RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, design: builder.Build());
        var generated = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var provider = new KeyedProvider(context);
        await using var work = new KeyedRepositoryUnitOfWork<CommandContext, string>(provider);
        var operations = Activator.CreateInstance(generated.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, work)!;
        operations.GetType().GetMethod("ReadAsync")!.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(KeyedRequest), typeof(CancellationToken));
        Func<Task> invalid = () => Call<Task<IReadOnlyList<string>>>(operations, "ReadAsync", new KeyedRequest("A", 0, ""), CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        provider.Keys.Should().BeEmpty();
        (await Call<Task<IReadOnlyList<string>>>(operations, "ReadAsync", new KeyedRequest("A", 1, ""), CancellationToken.None)).Should().Equal("Ada");
        (await Call<Task<IReadOnlyList<string>>>(operations, "ReadAsync", new KeyedRequest("A", 1, "Grace"), CancellationToken.None)).Should().BeEmpty();
        (await Call<Task<string?>>(operations, "LoadForUpdateAsync", new KeyedRequest("A", 1, "Ada"), CancellationToken.None)).Should().Be("Ada");
        Func<Task> switchKey = () => Call<Task<IReadOnlyList<string>>>(operations, "ReadAsync", new KeyedRequest("B", 1, ""), CancellationToken.None);
        await switchKey.Should().ThrowAsync<InvalidOperationException>().WithMessage("*switch context keys*");
        provider.Keys.Should().Equal("A");
    }

    [Fact]
    public async Task GlobalDtoFragmentsComposeOnRegisteredRootsInsideTheSharedTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new PipelineContext(new DbContextOptionsBuilder<PipelineContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(new PipelineRow { Id = 1, Category = "A", Amount = 10 }, new PipelineRow { Id = 2, Category = "B", Amount = 30 }, new PipelineRow { Id = 3, Category = "B", Amount = 60 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Fragments.Roots")
            .UseAsyncContextLeases().GenerateOnlyConfiguredEntities());
        var minimum = builder.HasFragment<PipelineReport, FragmentRequest>("Minimum", (report, limits) => report.Total >= limits.Minimum);
        var maximum = builder.HasFragment<PipelineReport>("Maximum", report => report.Total <= 50);
        builder.Entity<PipelineRow>().HasQuery("Report").FromProvider<FragmentReportRoot, FragmentRequest, PipelineReport>(request => request)
            .UseIf<FragmentRequest>(request => request.ApplyMinimum, minimum.And(maximum)).OrderBy(report => report.Total).Paged();
        var model = RepositoryModelFactory.Create(context, typeof(PipelineRow).Assembly, design: builder.Build());
        var generated = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var root = new FragmentReportRoot();
        await using var work = new RepositoryUnitOfWork<PipelineContext>(context);
        var operations = Activator.CreateInstance(generated.GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!, work, root)!;
        await using var transaction = await work.BeginTransactionAsync();
        var request = new FragmentRequest(20, 40, null, 0, true, false, false);
        var page = await Call<Task<PagedResult<PipelineReport>>>(operations, "ReportAsync", request, 1, 10, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Total.Should().Be(30);
        root.Calls.Should().Be(1);
        root.SawTransaction.Should().BeTrue();
        await transaction.RollbackAsync();
        model.Entities.Should().ContainSingle().Which.EntityTypeName.Should().Contain(nameof(PipelineRow));
    }

    [Fact]
    public async Task ComposedNavigationPredicatesInferDependenciesAndRelatedWritesInvalidateTheCache()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AdapterContext(new DbContextOptionsBuilder<AdapterContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Add(new AdapterOwner { Id = 1, Name = "Ada" });
        context.Rows.Add(new AdapterRow { Id = 1, Name = "Entry", Scope = 1, OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Fragments.Cache")
            .UseClosedCaching("fragment-fixture").GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<AdapterRow>();
        entity.WriteEnabled = false;
        var owner = entity.HasFragment<SqlNameRequest>("OwnerName", (row, selection) => row.Owner != null && row.Owner.Name == selection.Name);
        var scope = entity.HasFragment("Scope", other => other.Scope == 1);
        entity.HasQuery("Find").Cache().Use(owner.And(scope).WithRequestName("request")).Select(row => row.Id).List();
        var owners = builder.Entity<AdapterOwner>();
        owners.ReadEnabled = false;
        owners.HasCommand("Rename").Where(row => row.Id == 1).Update(update => update.SetProperty<string, string>(row => row.Name, (row, name) => name));
        var model = RepositoryModelFactory.Create(context, typeof(AdapterRow).Assembly, design: builder.Build());
        model.Entities.Single(item => item.EntityTypeName.Contains(nameof(AdapterRow), StringComparison.Ordinal)).Queries.Single().Cache.DependentEntityTypeNames
            .Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(AdapterOwner)));
        var generated = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var cache = new Cache();
        var operations = Activator.CreateInstance(generated.GetType(typeof(AdapterRow).Namespace + ".AdapterRowRepositoryOperations")!, context, cache)!;
        var ownerOperations = Activator.CreateInstance(generated.GetType(typeof(AdapterOwner).Namespace + ".AdapterOwnerRepositoryOperations")!, context, cache)!;
        var request = new SqlNameRequest("Ada");
        (await Call<Task<IReadOnlyList<int>>>(operations, "FindAsync", request, CancellationToken.None)).Should().Equal(1);
        (await Call<Task<IReadOnlyList<int>>>(operations, "FindAsync", request, CancellationToken.None)).Should().Equal(1);
        cache.Fills.Should().Be(1);
        (await Call<Task<int>>(ownerOperations, "RenameAsync", "Grace", CancellationToken.None)).Should().Be(1);
        cache.Invalidations.Should().Be(1);
        (await Call<Task<IReadOnlyList<int>>>(operations, "FindAsync", request, CancellationToken.None)).Should().BeEmpty();
        cache.Fills.Should().Be(2);
        (await Call<Task<IReadOnlyList<int>>>(operations, "FindAsync", new SqlNameRequest("Grace"), CancellationToken.None)).Should().Equal(1);
        cache.Fills.Should().Be(3);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static T Call<T>(object instance, string method, params object[] arguments) => (T)instance.GetType().GetMethod(method)!.Invoke(instance, arguments)!;

    private sealed class KeyedProvider(CommandContext context) : IKeyedDbContextLeaseProvider<CommandContext, string>
    {
        public List<string> Keys { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            return ValueTask.FromResult(DbContextLease.Borrowed(context, "fragment:" + key));
        }
    }

    private sealed class Cache : IRepositoryResultCache
    {
        private readonly Dictionary<string, (object? Value, IReadOnlyCollection<string> Tags)> entries = [];
        public int Fills { get; private set; }
        public int Invalidations { get; private set; }

        public async Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        {
            if (entries.TryGetValue(key, out var entry))
            {
                return (T)entry.Value!;
            }

            Fills++;
            var result = await factory(cancellationToken);
            entries[key] = (result, tags);
            return result;
        }

        public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Invalidations++;
            foreach (var key in entries.Where(entry => entry.Value.Tags.Intersect(tags, StringComparer.Ordinal).Any()).Select(entry => entry.Key).ToArray())
            {
                entries.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}

public sealed class FragmentReportRoot : IRepositoryQueryRoot<PipelineContext, FragmentRequest, PipelineReport>
{
    public int Calls { get; private set; }
    public bool SawTransaction { get; private set; }

    public IQueryable<PipelineReport> CreateQuery(PipelineContext context, FragmentRequest request)
    {
        Calls++;
        SawTransaction |= context.Database.CurrentTransaction is not null;
        return context.Rows.Select(row => new PipelineReport { Category = row.Category, Count = 1, Total = row.Amount });
    }
}