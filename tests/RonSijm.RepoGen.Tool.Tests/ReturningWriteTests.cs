using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningWriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCommandsReturnEveryDirectlyAffectedRowWithCompositeKeysConvertersAndComputedColumns(bool leases)
    {
        await using var fixture = await Fixture.CreateAsync();
        var context = fixture.Context;
        var model = Model(context, leases: leases, bindPort: true);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var operations = Operations(assembly, leases ? new ScopedDbContextLeaseProvider<ReturningContext>(context) : context);
        const string suffix = "'; DROP TABLE anything; --";
        var result = await Call<Task<MutationResult<ReturningDto>>>(operations, "IncreaseAsync", new ReturningRequest("item", 4, suffix, 1), CancellationToken.None);
        result.AffectedRows.Should().Be(2);
        result.Items.Select(item => item.Name).Should().BeEquivalentTo("item1" + suffix, "item2" + suffix);
        result.Items.Should().OnlyContain(item => item.Quantity == 5 && item.Revision == 2 && item.State == ReturningState.Ready);
        foreach (var item in result.Items)
        {
            item.Normalized.Should().Be(item.Name.ToUpperInvariant());
        }

        context.ChangeTracker.Entries().Should().BeEmpty();
        (await context.Rows.CountAsync(row => row.State == ReturningState.Ready)).Should().Be(2);
        var all = await context.Rows.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        all.Single(row => row.Archived).Quantity.Should().Be(1);
        all.Single(row => row.TenantId == 2).Quantity.Should().Be(1);
        var removed = await Call<Task<MutationResult<ReturningDto>>>(operations, "RemoveAsync", new ReturningRequest("item", 0, "", 2), CancellationToken.None);
        removed.AffectedRows.Should().Be(2);
        removed.Items.Should().BeEquivalentTo(result.Items);
        (await context.Rows.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await Call<Task<MutationResult<ReturningDto>>>(operations, "RemoveAsync", new ReturningRequest("item", 0, "", 2), CancellationToken.None)).Items.Should().BeEmpty();
        context.ChangeTracker.Entries().Should().BeEmpty();
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().Contain("MutationResult<").And.NotContain("IQueryable");
        operations.Should().BeAssignableTo<IReturningWrites>();
    }

    [Fact]
    public async Task ReturningWritesDoNotSaveOrSynchronizeUnrelatedTrackedState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tracked = await fixture.Context.Rows.SingleAsync(row => row.TenantId == 1 && row.Id == 1);
        tracked.Description = "pending only";
        var result = await Call<Task<MutationResult<ReturningDto>>>(Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)),
            new ScopedDbContextLeaseProvider<ReturningContext>(fixture.Context)), "IncreaseAsync", new ReturningRequest("item", 2, "", 1), CancellationToken.None);
        result.AffectedRows.Should().Be(2);
        result.Items.Should().OnlyContain(item => item.Quantity == 3 && item.Description == null);
        tracked.Quantity.Should().Be(1);
        tracked.Description.Should().Be("pending only");
        fixture.Context.ChangeTracker.Entries().Should().ContainSingle();
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.TenantId == 1 && row.Id == 1)).Description.Should().BeNull();
    }

    [Fact]
    public async Task ProjectionAndDatabaseFailuresRollBackStandaloneOperationsAndNeverNotifyObservers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new Recorder();
        await using var work = new RepositoryUnitOfWork<ReturningContext>(fixture.Context, [recorder]);
        var operations = Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), work);
        Func<Task> projection = async () => await Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await projection.Should().ThrowAsync<InvalidOperationException>().WithMessage("*returning projection failure*");
        (await fixture.Context.Rows.AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1);
        recorder.Calls.Should().Be(0);
        fixture.Context.Database.CurrentTransaction.Should().BeNull();
        Func<Task> constraint = async () => await Call<Task<MutationResult<ReturningDto>>>(operations, "IncreaseAsync", new ReturningRequest("item", -2, "", 1), CancellationToken.None);
        await constraint.Should().ThrowAsync<SqliteException>();
        (await fixture.Context.Rows.AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1);
        recorder.Calls.Should().Be(0);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ASharedTransactionUsesASavepointAndDefersObserversUntilItsCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new Recorder();
        await using var work = new RepositoryUnitOfWork<ReturningContext>(fixture.Context, [recorder]);
        var operations = Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), work);
        await using var transaction = await work.BeginTransactionAsync();
        (await Call<Task<MutationResult<ReturningDto>>>(operations, "IncreaseAsync", new ReturningRequest("item", 3, "", 1), CancellationToken.None)).AffectedRows.Should().Be(2);
        Func<Task> failed = async () => await Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await failed.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Context.Rows.AsNoTracking().Where(row => row.TenantId == 1).ToListAsync()).Should().OnlyContain(row => row.Quantity == 4);
        recorder.Calls.Should().Be(0);
        await transaction.CommitAsync();
        recorder.Calls.Should().Be(1);
        recorder.Tags.Should().Contain(typeof(ReturningRow).FullName!);
    }

    [Fact]
    public async Task RollingBackTheSharedTransactionRestoresUpdatesAndDeletesWithoutNotifications()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new Recorder();
        await using var work = new RepositoryUnitOfWork<ReturningContext>(fixture.Context, [recorder]);
        var operations = Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), work);
        await using var transaction = await work.BeginTransactionAsync();
        await Call<Task<MutationResult<ReturningDto>>>(operations, "IncreaseAsync", new ReturningRequest("item", 1, "", 1), CancellationToken.None);
        (await Call<Task<MutationResult<ReturningDto>>>(operations, "RemoveAsync", new ReturningRequest("item", 0, "", 2), CancellationToken.None)).AffectedRows.Should().Be(2);
        await transaction.RollbackAsync();
        recorder.Calls.Should().Be(0);
        (await fixture.Context.Rows.AsNoTracking().ToListAsync()).Should().HaveCount(3).And.OnlyContain(row => row.Quantity == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachesAreInvalidatedOnlyAtSuccessfulCommitAndMappingFailureLeavesThemValid(bool sharedTransaction)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new RecordingCache(new MemoryRepositoryResultCache(memory));
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context, caching: true));
        await using var work = new RepositoryUnitOfWork<ReturningContext>(new ScopedProvider(fixture.Context));
        var operations = Operations(assembly, work, cache);
        (await Call<Task<IReadOnlyList<int>>>(operations, "QuantitiesAsync", CancellationToken.None)).Should().Equal(1, 1);
        if (sharedTransaction)
        {
            await using var transaction = await work.BeginTransactionAsync();
            await Increase();
            cache.Invalidations.Should().Be(0);
            (await Call<Task<IReadOnlyList<int>>>(operations, "QuantitiesAsync", CancellationToken.None)).Should().Equal(3, 3);
            await transaction.CommitAsync();
        }
        else
        {
            await Increase();
        }

        cache.Invalidations.Should().Be(1);
        (await Call<Task<IReadOnlyList<int>>>(operations, "QuantitiesAsync", CancellationToken.None)).Should().Equal(3, 3);
        Func<Task> fail = async () => await Call<Task<MutationResult<int>>>(operations, "FailAsync", CancellationToken.None);
        await fail.Should().ThrowAsync<InvalidOperationException>();
        cache.Invalidations.Should().Be(1);
        (await Call<Task<IReadOnlyList<int>>>(operations, "QuantitiesAsync", CancellationToken.None)).Should().Equal(3, 3);
        Task Increase() => Call<Task<MutationResult<ReturningDto>>>(operations, "IncreaseAsync", new ReturningRequest("item", 2, "", 1), CancellationToken.None);
    }

    internal static RepositoryGenerationModel Model(ReturningContext context, bool leases = true, bool bindPort = false, bool caching = false)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Returning.Contracts").UseAsyncContextLeases(leases)
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities().IncludeApiDataContracts());
        if (caching)
        {
            builder.Configure(options => options.UseClosedCaching());
        }

        builder.Entity<ReturningRow>(entity =>
        {
            entity.HasVisibilityPolicy("tenant", row => row.TenantId == 1);
            if (bindPort)
            {
                entity.BindWritePort<IReturningWrites>();
            }

            entity.HasQuery("Quantities").Cache().OrderBy(row => row.Id).Select(row => row.Quantity).List();
            entity.HasCommand("Increase").Where<ReturningRequest>((row, request) => row.Name.StartsWith(request.Prefix))
                .Update(setters => setters.SetProperty<int, ReturningRequest>(row => row.Quantity, (row, request) => row.Quantity + request.Increment)
                    .SetProperty<string, ReturningRequest>(row => row.Name, (row, request) => row.Name + request.Suffix)
                    .SetProperty(row => row.State, ReturningState.Ready).SetProperty(row => row.Revision, row => row.Revision + 1))
                .AllowWrite(row => row.Revision).ExpectVersion<int, ReturningRequest>(row => row.Revision, (row, request) => request.ExpectedVersion)
                .ReturnsMany(row => new ReturningDto(row.Id, row.Name, row.Quantity, row.Description, row.Normalized, row.State, row.Revision));
            entity.HasCommand("Remove").Where<ReturningRequest>((row, request) => row.Name.StartsWith(request.Prefix))
                .Delete().ExpectVersion<int, ReturningRequest>(row => row.Revision, (row, request) => request.ExpectedVersion)
                .ReturnsMany(row => new ReturningDto(row.Id, row.Name, row.Quantity, row.Description, row.Normalized, row.State, row.Revision));
            entity.HasCommand("Fail").Update(setters => setters.SetProperty(row => row.Quantity, row => row.Quantity + 1))
                .ReturnsMany(row => ReturningMapping.Fail(row.Quantity));
        });
        return RepositoryModelFactory.Create(context, typeof(ReturningRow).Assembly, true, builder.Build());
    }

    private static object Operations(Assembly assembly, object source, IRepositoryResultCache? cache = null) => Activator.CreateInstance(
        assembly.GetType(typeof(ReturningRow).Namespace + ".ReturningRowRepositoryOperations")!, cache is null ? [source, new SqliteReturningWriter()] : [source, cache, new SqliteReturningWriter()])!;

    internal static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;

    [Fact]
    public async Task ClientOnlySetterExpressionsAreRejectedBeforeMutatingAnyRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Returning.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<ReturningRow>().HasCommand("ClientOnly").Update(setters => setters.SetProperty(row => row.Quantity, row => ReturningMapping.ClientOnly(row.Quantity)))
            .ReturnsMany(row => row.Quantity);
        var generated = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(fixture.Context, typeof(ReturningRow).Assembly, true, builder.Build()), rejectWarnings: true);
        Func<Task> execute = async () => await Call<Task<MutationResult<int>>>(Operations(generated, fixture.Context), "ClientOnlyAsync", CancellationToken.None);
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
        (await fixture.Context.Rows.AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1);
        fixture.Context.Database.CurrentTransaction.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualSetterValuesAndKeyDerivedValuesAreNotLostToProjectionDeduplication(bool keyValue)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Returning.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        var command = builder.Entity<ReturningRow>().HasCommand("Same").AllowWrite(row => row.Revision);
        if (keyValue)
        {
            command.Update(setters => setters.SetProperty(row => row.Quantity, row => row.Id).SetProperty(row => row.Revision, row => row.Id));
        }
        else
        {
            command.Update(setters => setters.SetProperty(row => row.Quantity, 7).SetProperty(row => row.Revision, 7));
        }

        command.ReturnsMany(row => row.Quantity == row.Revision);
        var generated = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(fixture.Context, typeof(ReturningRow).Assembly, true, builder.Build()), rejectWarnings: true);
        var result = await Call<Task<MutationResult<bool>>>(Operations(generated, fixture.Context), "SameAsync", CancellationToken.None);
        result.AffectedRows.Should().Be(3);
        result.Items.Should().OnlyContain(value => value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullAssignmentsRemainMatchedWithEitherEfNullSemantics(bool relationalNulls)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ReturningContext(new DbContextOptionsBuilder<ReturningContext>().UseSqlite(connection, options => options.UseRelationalNulls(relationalNulls)).Options);
        await context.Database.EnsureCreatedAsync();
        context.Rows.Add(new ReturningRow { TenantId = 1, Id = 1, Description = "before" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Returning.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<ReturningRow>().HasCommand("Clear").Update(setters => setters.SetProperty<string?>(row => row.Description, (string?)null))
            .ReturnsMany(row => row.Description!).ResultNullability<string>(shape => shape.Nullable());
        var model = RepositoryModelFactory.Create(context, typeof(ReturningRow).Assembly, true, builder.Build());
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().Contain("MutationResult<global::System.String?>");
        var result = await Call<Task<MutationResult<string?>>>(Operations(ClosedRepositoryTests.Compile(model, rejectWarnings: true), context), "ClearAsync", CancellationToken.None);
        result.AffectedRows.Should().Be(1);
        result.Items.Should().Equal(new string?[] { null });
        (await context.Rows.AsNoTracking().SingleAsync()).Description.Should().BeNull();
    }

    [Fact]
    public async Task CancellationAndNoMatchDoNotCreateCommittedChangesOrNotifyObservers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recorder = new Recorder();
        await using var work = new RepositoryUnitOfWork<ReturningContext>(fixture.Context, [recorder]);
        var operations = Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), work);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> canceled = async () => await Call<Task<MutationResult<ReturningDto>>>(operations, "IncreaseAsync", new ReturningRequest("item", 1, "", 1), cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        (await Call<Task<MutationResult<ReturningDto>>>(operations, "IncreaseAsync", new ReturningRequest("item", 1, "", 99), CancellationToken.None)).AffectedRows.Should().Be(0);
        recorder.Calls.Should().Be(0);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        (await fixture.Context.Rows.AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Quantity == 1);
    }

    [Fact]
    public async Task CompetingNativeVersionedUpdatesAndDeletesEachHaveOneWinner()
    {
        var file = Path.Combine(Path.GetTempPath(), "repogen-returning-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<ReturningContext>().UseSqlite("Data Source=" + file + ";Pooling=False;Default Timeout=15").Options;
            Assembly assembly;
            await using (var seed = new ReturningContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                seed.Rows.Add(new ReturningRow { TenantId = 1, Id = 1, Name = "item" });
                await seed.SaveChangesAsync();
                assembly = ClosedRepositoryTests.Compile(Model(seed));
            }

            var updates = await Race("IncreaseAsync", new ReturningRequest("item", 1, "", 1));
            updates.Count(result => result.AffectedRows == 1).Should().Be(1);
            updates.Count(result => result.AffectedRows == 0).Should().Be(5);
            var deletes = await Race("RemoveAsync", new ReturningRequest("item", 0, "", 2));
            deletes.Count(result => result.AffectedRows == 1).Should().Be(1);
            deletes.Count(result => result.AffectedRows == 0).Should().Be(5);
            await using var verify = new ReturningContext(options);
            (await verify.Rows.CountAsync()).Should().Be(0);

            Task<MutationResult<ReturningDto>[]> Race(string method, ReturningRequest request) => Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await using var context = new ReturningContext(options);
                var result = await Call<Task<MutationResult<ReturningDto>>>(Operations(assembly, new ScopedDbContextLeaseProvider<ReturningContext>(context)), method, request, CancellationToken.None);
                context.ChangeTracker.Entries().Should().BeEmpty();
                return result;
            })));
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private sealed class Fixture(ReturningContext context, SqliteConnection connection) : IAsyncDisposable
    {
        public ReturningContext Context { get; } = context;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ReturningContext(new DbContextOptionsBuilder<ReturningContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            context.Rows.AddRange(new ReturningRow { TenantId = 1, Id = 1, Name = "item1" }, new ReturningRow { TenantId = 1, Id = 2, Name = "item2" },
                new ReturningRow { TenantId = 2, Id = 1, Name = "item3" }, new ReturningRow { TenantId = 1, Id = 3, Name = "item4", Archived = true });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return new(context, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Recorder : IRepositoryCommitObserver
    {
        public int Calls { get; private set; }
        public IReadOnlyCollection<string> Tags { get; private set; } = [];

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Calls++;
            Tags = tags;
            return Task.CompletedTask;
        }
    }

    private sealed class ScopedProvider(ReturningContext context) : IDbContextLeaseProvider<ReturningContext>
    {
        public ValueTask<DbContextLease<ReturningContext>> AcquireAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(DbContextLease.Borrowed(context, "returning-fixture"));
    }

    private sealed class RecordingCache(IRepositoryResultCache inner) : IRepositoryResultCache
    {
        public int Invalidations { get; private set; }

        public Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
            => inner.GetOrCreateAsync(key, tags, options, factory, cancellationToken);

        public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Invalidations++;
            return inner.InvalidateAsync(tags, cancellationToken);
        }
    }
}

public sealed record ReturningRequest(string Prefix, int Increment, string Suffix, int ExpectedVersion);

public sealed record ReturningDto(int Id, string Name, int Quantity, string? Description, string Normalized, ReturningState State, int Revision);

public interface IReturningWrites
{
    Task<MutationResult<ReturningDto>> IncreaseAsync(ReturningRequest request, CancellationToken cancellationToken = default);
    Task<MutationResult<ReturningDto>> RemoveAsync(ReturningRequest request, CancellationToken cancellationToken = default);
}

public enum ReturningState
{
    Pending,
    Ready
}

public static class ReturningMapping
{
    public static int Fail(int value) => throw new InvalidOperationException("Intentional returning projection failure: " + value);
    public static int ClientOnly(int value) => value + 100;
}

public sealed class ReturningRow
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string? Description { get; set; }
    public string Normalized { get; set; } = "";
    public ReturningState State { get; set; }
    public bool Archived { get; set; }
    public int Revision { get; set; } = 1;
}

public sealed class ReturningContext(DbContextOptions<ReturningContext> options, string? shape = null) : DbContext(options)
{
    public string? Shape { get; } = shape;
    public DbSet<ReturningRow> Rows => Set<ReturningRow>();
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.ReplaceService<IModelCacheKeyFactory, ReturningModelKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReturningRow>();
        entity.ToTable("returning \" table", table =>
        {
            table.HasCheckConstraint("positive_quantity", "Quantity >= 0");
            if (Shape == "trigger")
            {
                table.HasTrigger("SomeTrigger");
            }
        });
        entity.HasKey(row => new { row.TenantId, row.Id });
        entity.HasQueryFilter(row => !row.Archived);
        entity.Property(row => row.Name).HasColumnName("label]field");
        entity.Property(row => row.Normalized).HasComputedColumnSql("upper(\"label]field\")");
        entity.Property(row => row.State).HasConversion<string>();
        entity.Property(row => row.Revision).IsConcurrencyToken();
        if (Shape == "shadow")
        {
            entity.Property<string>("Shadow");
        }

        if (Shape == "schema")
        {
            entity.ToTable("returning", "unsupported");
        }

        if (Shape == "keyless")
        {
            entity.HasNoKey();
        }
    }
}

public sealed class ReturningModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((ReturningContext)context).Shape, designTime);
}