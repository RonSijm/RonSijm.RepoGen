using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SaveParticipantIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedStagedCommandsAndSetBasedWritesShareParticipantRowsAndCacheCommit(bool commit)
    {
        await using var fixture = await Fixture.CreateAsync();
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context));
        var cache = new Cache();
        var observer = new Observer();
        await using var work = new RepositoryUnitOfWork<CommandContext>(new BorrowedProvider(fixture.Context), [observer]);
        var participant = new Participant();
        work.RegisterSaveParticipant(participant, participant);
        var operations = Operations(assembly, work, cache);
        (await ReadNames(operations)).Should().Equal("initial", "other");
        (await ReadParticipants(operations)).Should().BeEmpty();
        cache.Fills.Should().Be(2);
        await using (var transaction = await work.BeginTransactionAsync())
        {
            (await Call<Task<CommandResult>>(operations, "StageNameAsync", "tracked", CancellationToken.None)).Status.Should().Be(CommandStatus.Staged);
            participant.Calls.Should().Be(0);
            await fixture.Context.Rows.Where(row => row.Id == 2).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "bulk"));
            await work.RecordChangesAsync([typeof(CommandRow).FullName!]);
            participant.Calls.Should().Be(0);
            (await work.SaveChangesAsync()).Should().Be(2); // One tracked row and one participant row.
            (await ReadNames(operations)).Should().Equal("tracked", "bulk");
            (await ReadParticipants(operations)).Should().Equal(100);
            cache.Invalidations.Should().Be(0);
            cache.Fills.Should().Be(2);
            observer.Tags.Should().BeEmpty();
            if (commit)
            {
                await transaction.CommitAsync();
            }
            else
            {
                await transaction.RollbackAsync();
            }
        }

        cache.Invalidations.Should().Be(commit ? 1 : 0);
        if (commit)
        {
            observer.Tags.Should().ContainSingle().Which.Should().BeEquivalentTo(typeof(CommandRow).FullName!, typeof(CommandOwner).FullName!);
        }
        else
        {
            observer.Tags.Should().BeEmpty();
        }

        fixture.Context.ChangeTracker.Clear();
        await using var fresh = new RepositoryUnitOfWork<CommandContext>(new BorrowedProvider(fixture.Context));
        var freshOperations = Operations(assembly, fresh, cache);
        (await ReadNames(freshOperations)).Should().Equal(commit ? ["tracked", "bulk"] : ["initial", "other"]);
        (await ReadParticipants(freshOperations)).Should().Equal(commit ? [100] : []);
        cache.Fills.Should().Be(commit ? 4 : 2);
    }

    [Fact]
    public async Task SavingGeneratedCommandsPrepareInfrastructureAndKeepSavedResultSemantics()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cache = new Cache();
        var participant = new Participant();
        await using var work = new RepositoryUnitOfWork<CommandContext>(new BorrowedProvider(fixture.Context));
        work.RegisterSaveParticipant(participant, participant);
        var operations = Operations(ClosedRepositoryTests.Compile(Model(fixture.Context)), work, cache);
        await ReadNames(operations);
        await ReadParticipants(operations);
        Func<Task> noTransaction = async () => await Call<Task<CommandResult<string>>>(operations, "SaveNameAsync", "not persisted", CancellationToken.None);
        await noTransaction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicit transaction*");
        participant.Calls.Should().Be(0);
        fixture.Context.ChangeTracker.Entries<CommandRow>().Single().Entity.Name.Should().Be("initial");
        await using var transaction = await work.BeginTransactionAsync();
        var result = await Call<Task<CommandResult<string>>>(operations, "SaveNameAsync", "saved", CancellationToken.None);
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value.Should().Be("saved");
        result.AffectedRows.Should().Be(2);
        participant.Calls.Should().Be(1);
        cache.Invalidations.Should().Be(0);
        await transaction.CommitAsync();
        cache.Invalidations.Should().Be(1);
        (await ReadParticipants(operations)).Should().Equal(100);
        (await ReadNames(operations)).Should().Equal("saved", "other");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayUsesFreshParticipantTrackingStateAndOnlyTheWinningCommitNotifies(bool failureInParticipant)
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new RetryProvider(fixture.Connection);
        var observer = new Observer();
        var attempts = 0;
        var participants = new List<Participant>();
        var result = await RepositoryTransactionRunner.ExecuteAsync(provider, async (work, token) =>
        {
            var attempt = ++attempts;
            var participant = new Participant { Fail = failureInParticipant && attempt == 1 };
            participants.Add(participant);
            work.RegisterSaveParticipant(participant, participant);
            await using var lease = await work.AcquireAsync(token);
            lease.Context.Rows.Add(new CommandRow { Name = "attempt " + attempt, OwnerId = 1 });
            await work.SaveChangesAsync(token);
            if (!failureInParticipant && attempt == 1)
            {
                throw new FixtureTransientException();
            }

            return "success";
        }, [observer]);
        result.Should().Be("success");
        attempts.Should().Be(2);
        participants.Should().HaveCount(2);
        participants[0].Calls.Should().Be(1);
        participants[1].Calls.Should().Be(2);
        provider.Contexts.Should().HaveCount(3).And.OnlyContain(context => context.DisposeCount == 1);
        participants[0].Contexts[0].Should().NotBeSameAs(participants[1].Contexts[0]);
        observer.Tags.Should().ContainSingle().Which.Should().BeEquivalentTo(typeof(CommandRow).FullName!, typeof(CommandOwner).FullName!);
        (await fixture.Context.Rows.Where(row => row.Id > 2).AsNoTracking().Select(row => row.Name).ToListAsync()).Should().Equal("attempt 2");
        (await fixture.Context.Owners.CountAsync(owner => owner.Id == 100)).Should().Be(1);
    }

    [Fact]
    public async Task ParticipantWritesAreNotReplayedWhenAnObserverFailsAfterCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new RetryProvider(fixture.Connection);
        var observer = new Observer { Fail = true };
        var participant = new Participant();
        var attempts = 0;
        Func<Task> operation = async () => await RepositoryTransactionRunner.ExecuteAsync(provider, async (work, token) =>
        {
            attempts++;
            work.RegisterSaveParticipant(participant, participant);
            await using var lease = await work.AcquireAsync(token);
            lease.Context.Rows.Add(new CommandRow { Name = "committed", OwnerId = 1 });
            return 1;
        }, [observer]);
        await operation.Should().ThrowAsync<RepositoryCommitObserverException>();
        attempts.Should().Be(1);
        participant.Calls.Should().Be(1);
        observer.Tags.Should().ContainSingle();
        (await fixture.Context.Rows.CountAsync(row => row.Name == "committed")).Should().Be(1);
        (await fixture.Context.Owners.CountAsync(owner => owner.Id == 100)).Should().Be(1);
        provider.Contexts.Should().HaveCount(2).And.OnlyContain(context => context.DisposeCount == 1);
    }

    private static RepositoryGenerationModel Model(CommandContext context)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Participant.Contracts")
            .GenerateOnlyConfiguredEntities().UseAsyncContextLeases().UseClosedCaching());
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasQuery("Names").Cache().OrderBy(row => row.Id).Select(row => row.Name).List();
            entity.HasQuery("ParticipantIds").Cache().From(sources => sources.Set<CommandOwner>()
                .Where(owner => owner.Id >= 100).OrderBy(owner => owner.Id).Select(owner => owner.Id)).List();
            entity.HasCommand("StageName").Where(row => row.Id == 1)
                .Patch(setters => setters.SetProperty<string, string>(row => row.Name, (row, name) => name)).Stage();
            entity.HasCommand("SaveName").Where(row => row.Id == 1)
                .Patch(setters => setters.SetProperty<string, string>(row => row.Name, (row, name) => name)).Save().Returns(row => row.Name);
        });
        return RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build());
    }

    private static object Operations(System.Reflection.Assembly assembly, IDbContextLeaseProvider<CommandContext> provider, IRepositoryResultCache cache) =>
        Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, provider, cache)!;

    private static Task<IReadOnlyList<string>> ReadNames(object operations) => Call<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None);
    private static Task<IReadOnlyList<int>> ReadParticipants(object operations) => Call<Task<IReadOnlyList<int>>>(operations, "ParticipantIdsAsync", CancellationToken.None);
    private static T Call<T>(object operations, string method, params object[] arguments) => (T)operations.GetType().GetMethod(method)!.Invoke(operations, arguments)!;

    private sealed class Participant : IRepositorySaveParticipant<CommandContext>
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public List<CommandContext> Contexts { get; } = [];

        public ValueTask SavingAsync(CommandContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Contexts.Add(context);
            if (context.ChangeTracker.Entries<CommandRow>().Any(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                context.Owners.Add(new CommandOwner { Id = 100 });
            }

            if (Fail)
            {
                throw new FixtureTransientException();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Observer : IRepositoryCommitObserver
    {
        public bool Fail { get; init; }
        public List<IReadOnlyCollection<string>> Tags { get; } = [];

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Tags.Add(tags);
            return Fail ? Task.FromException(new FixtureTransientException()) : Task.CompletedTask;
        }
    }

    private sealed class BorrowedProvider(CommandContext context) : IDbContextLeaseProvider<CommandContext>
    {
        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(DbContextLease.Borrowed(context, "participant-fixture"));
        }
    }

    private sealed class RetryProvider(SqliteConnection connection) : IDbContextLeaseProvider<CommandContext>
    {
        public List<CommandContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).ReplaceService<IExecutionStrategyFactory, FixtureStrategyFactory>().Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context, "participant-fixture"));
        }
    }

    private sealed class Cache : IRepositoryResultCache
    {
        private readonly Dictionary<string, (object? Value, IReadOnlyCollection<string> Tags)> entries = [];
        public int Fills { get; private set; }
        public int Invalidations { get; private set; }

        public async Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            Invalidations++;
            foreach (var key in entries.Where(entry => entry.Value.Tags.Intersect(tags).Any()).Select(entry => entry.Key).ToArray())
            {
                entries.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class Fixture(SqliteConnection connection, CommandContext context) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public CommandContext Context { get; } = context;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            context.Owners.AddRange(new CommandOwner { Id = 1 }, new CommandOwner { Id = 2 });
            context.Rows.AddRange(new CommandRow { Id = 1, Name = "initial", OwnerId = 1 }, new CommandRow { Id = 2, Name = "other", OwnerId = 2 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return new(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}