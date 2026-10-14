using System.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class ContextLifetimeTests
{
    [Fact]
    public async Task LeaseDisposesOwnedContextExactlyOnceButNeverBorrowedContext()
    {
        await using var store = await LifetimeStore.CreateAsync();
        var borrowed = DbContextLease.Borrowed(store.Context);
        await borrowed.DisposeAsync();
        await borrowed.DisposeAsync();
        store.Context.DisposeCount.Should().Be(0);
        Action read = () => _ = borrowed.Context;
        read.Should().Throw<ObjectDisposedException>();

        var ownedContext = store.CreateContext();
        var owned = DbContextLease.Owned(ownedContext);
        await owned.DisposeAsync();
        await owned.DisposeAsync();
        ownedContext.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task UnitOfWorkRetainsOneOwnedContextAcrossOperations()
    {
        await using var store = await LifetimeStore.CreateAsync();
        var provider = new TestLeaseProvider(store);
        var work = new RepositoryUnitOfWork<LifetimeContext>(provider);
        await using (var first = await work.AcquireAsync())
        {
            first.Context.Rows.Add(new LifetimeRow { Name = "one" });
        }

        await using (var second = await work.AcquireAsync())
        {
            second.Context.Should().BeSameAs(provider.Context);
            second.Context.Rows.Add(new LifetimeRow { Name = "two" });
        }

        provider.Count.Should().Be(1);
        provider.Context!.DisposeCount.Should().Be(0);
        (await work.SaveChangesAsync()).Should().Be(2);
        await work.DisposeAsync();
        await work.DisposeAsync();
        provider.Context.DisposeCount.Should().Be(1);
        (await store.Context.Rows.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BorrowedUnitOfWorkLeavesContextAlive()
    {
        await using var store = await LifetimeStore.CreateAsync();
        await using (var work = new RepositoryUnitOfWork<LifetimeContext>(store.Context))
        {
            await using var lease = await work.AcquireAsync();
            lease.Context.Rows.Add(new LifetimeRow { Name = "borrowed" });
            await work.SaveChangesAsync();
        }

        store.Context.DisposeCount.Should().Be(0);
        (await store.Context.Rows.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ObserversRunOnlyAfterExplicitCommitAndTagsAreDeduplicated()
    {
        await using var store = await LifetimeStore.CreateAsync();
        var observer = new RecordingObserver();
        await using var work = new RepositoryUnitOfWork<LifetimeContext>(store.Context, [observer], static _ => ["rows", "summary"]);
        await using var transaction = await work.BeginTransactionAsync();
        store.Context.Rows.Add(new LifetimeRow { Name = "saved" });
        await work.SaveChangesAsync();
        await work.RecordChangesAsync(["rows", "reports"]);
        observer.Calls.Should().BeEmpty();
        await transaction.CommitAsync();
        observer.Calls.Should().ContainSingle();
        observer.Calls[0].Should().Equal("reports", "rows", "summary");
        (await store.Context.Rows.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RollbackDoesNotNotifyAndPoisonedWorkCannotBeReused()
    {
        await using var store = await LifetimeStore.CreateAsync();
        var observer = new RecordingObserver();
        await using var work = new RepositoryUnitOfWork<LifetimeContext>(store.Context, [observer]);
        await using (var transaction = await work.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            store.Context.Rows.Add(new LifetimeRow { Name = "rolled back" });
            await work.SaveChangesAsync();
        }

        observer.Calls.Should().BeEmpty();
        (await store.Context.Rows.CountAsync()).Should().Be(0);
        Func<Task> reuse = async () => await work.AcquireAsync();
        await reuse.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rolled-back*");
    }

    [Fact]
    public async Task ObserverFailureReportsCommittedOutcomeAndStillNotifiesOtherObservers()
    {
        await using var store = await LifetimeStore.CreateAsync();
        var successful = new RecordingObserver();
        await using var work = new RepositoryUnitOfWork<LifetimeContext>(store.Context, [new FailingObserver(), successful]);
        store.Context.Rows.Add(new LifetimeRow { Name = "committed" });
        Func<Task> save = () => work.SaveChangesAsync();
        await save.Should().ThrowAsync<RepositoryCommitObserverException>();
        successful.Calls.Should().ContainSingle();
        (await store.Context.Rows.CountAsync()).Should().Be(1);
        (await work.SaveChangesAsync()).Should().Be(0);
        successful.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task InvalidTagsFailBeforeSaving()
    {
        await using var store = await LifetimeStore.CreateAsync();
        await using var work = new RepositoryUnitOfWork<LifetimeContext>(store.Context, tagSelector: static _ => [""]);
        store.Context.Rows.Add(new LifetimeRow { Name = "not saved" });
        Func<Task> save = () => work.SaveChangesAsync();
        await save.Should().ThrowAsync<ArgumentException>();
        (await store.Context.Rows.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CanceledAcquireDoesNotCreateContextAndExternalTransactionsAreRejected()
    {
        await using var store = await LifetimeStore.CreateAsync();
        var provider = new TestLeaseProvider(store);
        await using var work = new RepositoryUnitOfWork<LifetimeContext>(provider);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> acquire = async () => await work.AcquireAsync(cancellation.Token);
        await acquire.Should().ThrowAsync<OperationCanceledException>();
        provider.Count.Should().Be(0);

        await using var transaction = await store.Context.Database.BeginTransactionAsync();
        await using var borrowed = new RepositoryUnitOfWork<LifetimeContext>(store.Context);
        Func<Task> external = async () => await borrowed.AcquireAsync();
        await external.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not owned*");
    }

    private sealed class RecordingObserver : IRepositoryCommitObserver
    {
        public List<IReadOnlyCollection<string>> Calls { get; } = [];

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Calls.Add(tags);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FixedKeysKeepSeparateDatabasesIsolatedAndUnitOfWorkDoesNotReacquire()
    {
        await using var firstStore = await LifetimeStore.CreateAsync();
        await using var secondStore = await LifetimeStore.CreateAsync();
        var keyed = new KeyedTestProvider(new Dictionary<string, LifetimeStore>(StringComparer.Ordinal) { ["first"] = firstStore, ["second"] = secondStore });
        var firstProvider = new FixedKeyDbContextLeaseProvider<LifetimeContext, string>(keyed, "first");
        var secondProvider = new FixedKeyDbContextLeaseProvider<LifetimeContext, string>(keyed, "second");
        await using (var work = new RepositoryUnitOfWork<LifetimeContext>(firstProvider))
        {
            await using (var lease = await work.AcquireAsync())
            {
                lease.Context.Rows.Add(new LifetimeRow { Name = "first" });
            }

            await using (var lease = await work.AcquireAsync())
            {
                lease.Context.Rows.Add(new LifetimeRow { Name = "also first" });
            }

            await work.SaveChangesAsync();
        }

        await using (var lease = await secondProvider.AcquireAsync())
        {
            lease.Context.Rows.Add(new LifetimeRow { Name = "second" });
            await lease.Context.SaveChangesAsync();
        }

        keyed.Keys.Should().Equal("first", "second");
        (await firstStore.Context.Rows.Select(static row => row.Name).ToListAsync()).Should().BeEquivalentTo("first", "also first");
        (await secondStore.Context.Rows.Select(static row => row.Name).ToListAsync()).Should().Equal("second");
    }

    [Fact]
    public async Task FactoryAdapterUsesAsyncAcquisitionAndOwnsItsContext()
    {
        await using var store = await LifetimeStore.CreateAsync();
        var factory = new AsyncTestFactory(store);
        var provider = new FactoryDbContextLeaseProvider<LifetimeContext>(factory);
        var lease = await provider.AcquireAsync();
        var context = lease.Context;
        lease.OwnsContext.Should().BeTrue();
        factory.Calls.Should().Be(1);
        await lease.DisposeAsync();
        context.DisposeCount.Should().Be(1);
    }

    private sealed class AsyncTestFactory(LifetimeStore store) : IDbContextFactory<LifetimeContext>
    {
        public int Calls { get; private set; }
        public LifetimeContext CreateDbContext() => throw new InvalidOperationException("Synchronous acquisition must not be used.");

        public Task<LifetimeContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(store.CreateContext());
        }
    }

    private sealed class KeyedTestProvider(IReadOnlyDictionary<string, LifetimeStore> stores) : IKeyedDbContextLeaseProvider<LifetimeContext, string>
    {
        public List<string> Keys { get; } = [];

        public ValueTask<DbContextLease<LifetimeContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            return ValueTask.FromResult(DbContextLease.Owned(stores[key].CreateContext()));
        }
    }

    private sealed class FailingObserver : IRepositoryCommitObserver
    {
        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("cache unavailable"));
    }

    private sealed class TestLeaseProvider(LifetimeStore store) : IDbContextLeaseProvider<LifetimeContext>
    {
        public int Count { get; private set; }
        public LifetimeContext? Context { get; private set; }

        public ValueTask<DbContextLease<LifetimeContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            Context = store.CreateContext();
            return ValueTask.FromResult(DbContextLease.Owned(Context));
        }
    }

    private sealed class LifetimeStore(SqliteConnection connection, LifetimeContext context) : IAsyncDisposable
    {
        public LifetimeContext Context { get; } = context;
        public LifetimeContext CreateContext() => new(new DbContextOptionsBuilder<LifetimeContext>().UseSqlite(connection).Options);

        public static async Task<LifetimeStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new LifetimeContext(new DbContextOptionsBuilder<LifetimeContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new LifetimeStore(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class LifetimeContext(DbContextOptions<LifetimeContext> options) : DbContext(options)
    {
        public DbSet<LifetimeRow> Rows => Set<LifetimeRow>();
        public int DisposeCount { get; private set; }

        public override ValueTask DisposeAsync()
        {
            DisposeCount++;
            return base.DisposeAsync();
        }
    }

    private sealed class LifetimeRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}