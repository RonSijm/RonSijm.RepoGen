using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ClosedCachingTests
{
    [Fact]
    public async Task ASealedDirectContextSupportsCachedTrackedAndAtomicWritesWithoutALeaseProvider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Cache.Contracts")
            .UseClosedCaching("sealed-fixture").UseAtomicWrites<SqliteAtomicWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasQuery("Names").Cache().OrderBy(row => row.Id).Select(row => row.Name).List();
            entity.HasCommand("Create").Create<AtomicRequest>(request => new CommandRow { Name = request.Name, OwnerId = request.OwnerId, Revision = 1 })
                .AllowWrite(row => row.OwnerId).AllowWrite(row => row.Revision).Save().Returns(row => row.Name);
            entity.HasCommand("Ensure").Create<AtomicRequest>(request => new CommandRow { Name = request.Name, OwnerId = request.OwnerId, Revision = 1 })
                .AllowWrite(row => row.OwnerId).AllowWrite(row => row.Revision).InsertIfAbsent(row => row.Name).Returns(row => row.Name);
        });
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build()));
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context, new MemoryRepositoryResultCache(memory), new SqliteAtomicWriter())!;
        (await Call<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)).Should().BeEmpty();
        (await Call<Task<CommandResult<string>>>(operations, "CreateAsync", new AtomicRequest("tracked", null, 1, 0, 1), CancellationToken.None)).Status.Should().Be(CommandStatus.Applied);
        (await Call<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)).Should().Equal("tracked");
        (await Call<Task<CommandResult<string>>>(operations, "EnsureAsync", new AtomicRequest("atomic", null, 1, 0, 1), CancellationToken.None)).Status.Should().Be(CommandStatus.Inserted);
        (await Call<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)).Should().Equal("tracked", "atomic");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachingCanBeDisabledWithoutRetainingAnAdapterOrScopeRequirement(bool genericSwitch)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options =>
        {
            options.UseSimpleDefaults().UseClosedRepositories("Cache.Contracts").UseClosedCaching().GenerateOnlyConfiguredEntities();
            if (genericSwitch)
            {
                options.GenerateCacheDecorator(false);
            }
            else
            {
                options.UseClosedCaching(false);
            }
        });
        builder.Entity<CommandRow>().HasQuery("Names").Cache().Select(row => row.Name).List();
        var model = RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build());
        RepositorySourceRenderer.Render(model).Should().NotContain("IRepositoryResultCache").And.NotContain("__repoGenScope");
        var assembly = ClosedRepositoryTests.Compile(model);
        Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context).Should().NotBeNull();
    }

    [Fact]
    public async Task EqualArgumentsArePartitionedByTheAcquiredScopeAndWritesInvalidateOnlyThatScope()
    {
        await using var first = await Fixture.CreateAsync("first");
        await using var second = await Fixture.CreateAsync("second");
        var generated = ClosedRepositoryTests.Compile(Model(first.Context));
        var cache = new TestCache();
        var a = Operations(generated, new Provider(first.Context, "database:A/user:1"), cache);
        var b = Operations(generated, new Provider(second.Context, "database:B/user:1"), cache);
        (await Read(a)).Should().Equal("first");
        (await Read(b)).Should().Equal("second");
        await Read(a);
        await Read(b);
        cache.Fills.Should().Be(2);
        (await Call<Task<int>>(a, "RenameAsync", "changed", CancellationToken.None)).Should().Be(1);
        (await Read(a)).Should().Equal("changed");
        (await Read(b)).Should().Equal("second");
        cache.Fills.Should().Be(3);
        var noScope = Operations(generated, new Provider(first.Context, null), cache);
        Func<Task> invalid = async () => await Read(noScope);
        await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ScopeKey*");
        cache.Fills.Should().Be(3);
    }

    [Fact]
    public async Task TransactionsBypassCacheAndInvalidateOnlyAfterCommitNotRollback()
    {
        await using var fixture = await Fixture.CreateAsync("initial");
        var generated = ClosedRepositoryTests.Compile(Model(fixture.Context));
        var cache = new TestCache();
        await using var work = new RepositoryUnitOfWork<CommandContext>(new Provider(fixture.Context, "database:A"));
        var operations = Operations(generated, work, cache);
        await Read(operations);
        await using (var transaction = await work.BeginTransactionAsync())
        {
            await Call<Task<int>>(operations, "RenameAsync", "committed", CancellationToken.None);
            (await Read(operations)).Should().Equal("committed");
            cache.Invalidations.Should().Be(0);
            cache.Fills.Should().Be(1);
            await transaction.CommitAsync();
        }

        cache.Invalidations.Should().Be(1);
        (await Read(operations)).Should().Equal("committed");
        await using var rollbackWork = new RepositoryUnitOfWork<CommandContext>(new Provider(fixture.Context, "database:A"));
        var rollbackOperations = Operations(generated, rollbackWork, cache);
        await using (var transaction = await rollbackWork.BeginTransactionAsync())
        {
            await Call<Task<int>>(rollbackOperations, "RenameAsync", "rollback", CancellationToken.None);
            await transaction.RollbackAsync();
        }

        cache.Invalidations.Should().Be(1);
        cache.Fills.Should().Be(2);
        (await Read(operations)).Should().Equal("committed");
    }

    [Fact]
    public async Task SharedSavesInvalidateRelatedTagsAndPostSaveFailuresDoNotLeaveStaleEntries()
    {
        await using var fixture = await Fixture.CreateAsync("initial");
        var cache = new TestCache();
        var generated = ClosedRepositoryTests.Compile(Model(fixture.Context));
        await using var work = new RepositoryUnitOfWork<CommandContext>(new Provider(fixture.Context, "database:A"));
        var operations = Operations(generated, work, cache);
        await Read(operations);
        fixture.Context.Rows.Single().Name = "tracked";
        await Read(operations); // Bypasses rather than returning an already cached result.
        cache.Fills.Should().Be(1);
        await work.SaveChangesAsync();
        cache.Invalidations.Should().Be(1);
        (await Read(operations)).Should().Equal("tracked");
        var direct = Operations(generated, new Provider(fixture.Context, "database:A"), cache);
        Func<Task> mapping = async () => await Call<Task<CommandResult<CommandDto>>>(direct, "CreateFailAsync", "saved", CancellationToken.None);
        await mapping.Should().ThrowAsync<RepositorySavedResultException>();
        (await Read(direct)).Should().BeEquivalentTo("tracked", "saved");
        cache.FailInvalidation = true;
        Func<Task> invalidation = async () => await Call<Task<int>>(direct, "RenameAsync", "persisted", CancellationToken.None);
        await invalidation.Should().ThrowAsync<RepositoryCommitObserverException>();
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Name.Should().Be("persisted");
    }

    [Fact]
    public async Task UncoordinatedTransactionalWritesAreRejectedBeforeMutation()
    {
        await using var fixture = await Fixture.CreateAsync("initial");
        var operations = Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), new Provider(fixture.Context, "database:A"), new TestCache());
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        Func<Task> write = async () => await Call<Task<int>>(operations, "RenameAsync", "unsafe", CancellationToken.None);
        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*commit-coordinated*");
        (await fixture.Context.Rows.AsNoTracking().SingleAsync()).Name.Should().Be("initial");
    }

    private static RepositoryGenerationModel Model(CommandContext context)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Cache.Contracts").UseAsyncContextLeases().UseClosedCaching().GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasQuery("Names").Cache(cache => cache.For(TimeSpan.FromMinutes(5)).DependsOn<CommandOwner>()).Select(row => row.Name).List();
            entity.HasCommand("Rename").Where(row => row.Id == 1).Update(update => update.SetProperty<string, string>(row => row.Name, (row, name) => name));
            entity.HasCommand("CreateFail").Create<string>(name => new CommandRow { Name = name, OwnerId = 1 }).AllowWrite(row => row.OwnerId).Save().Returns(row => CommandMapping.Fail(row));
        });
        return RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
    }

    private static object Operations(System.Reflection.Assembly generated, IDbContextLeaseProvider<CommandContext> provider, IRepositoryResultCache cache) =>
        Activator.CreateInstance(generated.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, provider, cache)!;

    private static Task<IReadOnlyList<string>> Read(object instance) => Call<Task<IReadOnlyList<string>>>(instance, "NamesAsync", CancellationToken.None);
    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;

    private sealed class Provider(CommandContext context, string? scope) : IDbContextLeaseProvider<CommandContext>
    {
        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(DbContextLease.Borrowed(context, scope));
    }

    private sealed class TestCache : IRepositoryResultCache
    {
        private readonly Dictionary<string, (object? Value, IReadOnlyCollection<string> Tags)> entries = [];
        public int Fills { get; private set; }
        public int Invalidations { get; private set; }
        public bool FailInvalidation { get; set; }

        public async Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        {
            if (entries.TryGetValue(key, out var entry))
            {
                return (T)entry.Value!;
            }

            Fills++;
            var value = await factory(cancellationToken);
            entries[key] = (value, tags);
            return value;
        }

        public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            if (FailInvalidation)
            {
                throw new InvalidOperationException("Intentional cache failure");
            }

            Invalidations++;
            foreach (var key in entries.Where(entry => entry.Value.Tags.Intersect(tags, StringComparer.Ordinal).Any()).Select(static entry => entry.Key).ToArray())
            {
                entries.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class Fixture(SqliteConnection connection, CommandContext context) : IAsyncDisposable
    {
        public CommandContext Context { get; } = context;

        public static async Task<Fixture> CreateAsync(string name)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            context.Owners.Add(new CommandOwner { Id = 1 });
            context.Rows.Add(new CommandRow { Id = 1, Name = name, OwnerId = 1 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return new(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}