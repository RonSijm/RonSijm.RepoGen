using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class SaveParticipantTests
{
    [Fact]
    public async Task ParticipantsRequireAnExplicitTransactionBeforeInvokingAnyCallback()
    {
        await using var fixture = await Fixture.CreateAsync();
        var participant = JournalParticipant();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context);
        work.RegisterSaveParticipant(participant, participant);
        fixture.Context.Rows.Add(new ParticipantRow { Name = "one" });
        await work.Invoking(w => w.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicit transaction*");
        participant.Calls.Should().Be(0);
        (await fixture.Context.Rows.CountAsync()).Should().Be(0);
        await using var transaction = await work.BeginTransactionAsync();
        (await work.SaveChangesAsync()).Should().Be(2);
        participant.Contexts.Should().ContainSingle().Which.Should().BeSameAs(fixture.Context);
        participant.HadTransaction.Should().Equal(true);
        await transaction.CommitAsync();
        (await fixture.Context.Journal.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParticipantRowsAndDependencyTagsShareTheRealCommitBoundary(bool commit)
    {
        await using var fixture = await Fixture.CreateAsync();
        var observer = new Observer();
        var participant = JournalParticipant();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context, [observer]);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "saved together" });
        (await work.SaveChangesAsync(["explicit-dependency"])).Should().Be(2);
        (await fixture.Context.Rows.CountAsync()).Should().Be(1);
        (await fixture.Context.Journal.CountAsync()).Should().Be(1);
        observer.Tags.Should().BeEmpty();
        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        (await fixture.Context.Rows.CountAsync()).Should().Be(commit ? 1 : 0);
        (await fixture.Context.Journal.CountAsync()).Should().Be(commit ? 1 : 0);
        if (commit)
        {
            observer.Tags.Should().ContainSingle().Which.Should().BeEquivalentTo(
                typeof(ParticipantRow).FullName!, typeof(ParticipantJournal).FullName!, "explicit-dependency");
        }
        else
        {
            observer.Tags.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task RepeatedSavesPrepareOnlyCurrentTrackedChangesAndNotifyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        var participant = JournalParticipant();
        var observer = new Observer();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context, [observer]);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "first" });
        (await work.SaveChangesAsync()).Should().Be(2);
        fixture.Context.Rows.Add(new ParticipantRow { Name = "second" });
        (await work.SaveChangesAsync()).Should().Be(2);
        (await work.SaveChangesAsync()).Should().Be(0);
        participant.Calls.Should().Be(3);
        observer.Tags.Should().BeEmpty();
        await transaction.CommitAsync();
        (await fixture.Context.Journal.CountAsync()).Should().Be(2);
        observer.Tags.Should().ContainSingle();
    }

    [Fact]
    public async Task RegistrationUsesReferenceIdentityAndStableOrderBeforeTheFirstSave()
    {
        await using var fixture = await Fixture.CreateAsync();
        var order = new List<string>();
        var first = new Participant((context, _) =>
        {
            order.Add("first");
            context.Journal.Add(new ParticipantJournal { Message = "prepared" });
            return ValueTask.CompletedTask;
        });
        var second = new Participant((context, _) =>
        {
            order.Add("second");
            context.ChangeTracker.Entries<ParticipantJournal>().Should().ContainSingle();
            return ValueTask.CompletedTask;
        });
        var replaced = new Participant((_, _) => throw new InvalidOperationException("Must not replace the first registration."));
        var firstIdentity = new Identity(1);
        var secondIdentity = new Identity(1);
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context);
        work.RegisterSaveParticipant(firstIdentity, first);
        work.RegisterSaveParticipant(firstIdentity, replaced);
        work.RegisterSaveParticipant(secondIdentity, second);
        await using var transaction = await work.BeginTransactionAsync();
        (await work.SaveChangesAsync()).Should().Be(1);
        order.Should().Equal("first", "second");
        replaced.Calls.Should().Be(0);
        work.RegisterSaveParticipant(firstIdentity, first); // Existing identity remains an idempotent no-op.
        work.Invoking(w => w.RegisterSaveParticipant(new object(), replaced)).Should().Throw<InvalidOperationException>().WithMessage("*before*first save*");
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task UnconfiguredWorkKeepsOrdinarySavingAndExistingConstructorSignatures()
    {
        await using var fixture = await Fixture.CreateAsync();
        var observer = new Observer();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context, [observer]);
        fixture.Context.Rows.Add(new ParticipantRow { Name = "simple" });
        (await work.SaveChangesAsync()).Should().Be(1);
        observer.Tags.Should().ContainSingle();
        typeof(RepositoryUnitOfWork<ParticipantContext>).GetConstructor([
            typeof(ParticipantContext), typeof(IEnumerable<IRepositoryCommitObserver>), typeof(Func<Type, IEnumerable<string>>)
        ]).Should().NotBeNull();
        typeof(RepositoryUnitOfWork<ParticipantContext>).GetConstructor([
            typeof(IDbContextLeaseProvider<ParticipantContext>), typeof(IEnumerable<IRepositoryCommitObserver>), typeof(Func<Type, IEnumerable<string>>)
        ]).Should().NotBeNull();
        work.Invoking(w => w.RegisterSaveParticipant(new object(), JournalParticipant())).Should().Throw<InvalidOperationException>().WithMessage("*first save*");
    }

    [Fact]
    public async Task CommitAndSetBasedChangeRecordingNeverImplicitlyInvokeParticipantsOrSave()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "initial" });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var participant = JournalParticipant();
        var observer = new Observer();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context, [observer]);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        await fixture.Context.Rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "set-based"));
        await work.RecordChangesAsync(["set-based"]);
        fixture.Context.Rows.Add(new ParticipantRow { Name = "still staged" });
        await transaction.CommitAsync();
        participant.Calls.Should().Be(0);
        (await fixture.Context.Rows.AsNoTracking().Select(row => row.Name).ToListAsync()).Should().Equal("set-based");
        (await fixture.Context.Journal.CountAsync()).Should().Be(0);
        observer.Tags.Should().ContainSingle().Which.Should().Equal("set-based");
    }

    [Fact]
    public async Task PreparationFailureRequiresRollbackAndPreservesTheOriginalException()
    {
        await using var fixture = await Fixture.CreateAsync();
        var failure = new InvalidOperationException("participant failed");
        var observer = new Observer();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context, [observer]);
        work.RegisterSaveParticipant(new object(), new Participant((context, _) =>
        {
            context.Journal.Add(new ParticipantJournal { Message = "not saved" });
            throw failure;
        }));
        await using var transaction = await work.BeginTransactionAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "not saved" });
        (await work.Invoking(w => w.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        await AssertFailedWorkAsync(work, transaction);
        await transaction.RollbackAsync();
        (await fixture.Context.Rows.CountAsync()).Should().Be(0);
        (await fixture.Context.Journal.CountAsync()).Should().Be(0);
        observer.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeSaveFailureAlsoPreventsParticipantReplayAndCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Journal.Add(new ParticipantJournal { Message = "duplicate" });
        await fixture.Context.SaveChangesAsync();
        var participant = new Participant((context, _) =>
        {
            context.Journal.Add(new ParticipantJournal { Message = "duplicate" });
            return ValueTask.CompletedTask;
        });
        var observer = new Observer();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context, [observer]);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "rolled back" });
        await work.Invoking(w => w.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        await AssertFailedWorkAsync(work, transaction);
        participant.Calls.Should().Be(1);
        await transaction.RollbackAsync();
        (await fixture.Context.Rows.CountAsync()).Should().Be(0);
        (await fixture.Context.Journal.CountAsync()).Should().Be(1);
        observer.Tags.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailedLaterPreparationRollsBackEarlierTrackedAndSetBasedWrites(bool dispose)
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new KeyedProvider(fixture);
        var observer = new Observer();
        var calls = 0;
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(
            new FixedKeyDbContextLeaseProvider<ParticipantContext, string>(provider, "first"), [observer]);
        work.RegisterSaveParticipant(new object(), new Participant((context, _) =>
        {
            context.Journal.Add(new ParticipantJournal { Message = "save " + ++calls });
            if (calls == 2)
            {
                throw new InvalidOperationException("later preparation failed");
            }

            return ValueTask.CompletedTask;
        }));
        await using var transaction = await work.BeginTransactionAsync();
        await using var lease = await work.AcquireAsync();
        lease.Context.Rows.Add(new ParticipantRow { Name = "first" });
        (await work.SaveChangesAsync()).Should().Be(2);
        await lease.Context.Rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "bulk"));
        await work.RecordChangesAsync(["bulk"]);
        lease.Context.Rows.Add(new ParticipantRow { Name = "second" });
        await work.Invoking(w => w.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("later preparation failed");
        await AssertFailedWorkAsync(work, transaction);
        if (dispose)
        {
            await work.DisposeAsync();
        }
        else
        {
            await transaction.RollbackAsync();
            await work.DisposeAsync();
        }

        provider.Context!.DisposeCount.Should().Be(1);
        observer.Tags.Should().BeEmpty();
        (await fixture.Context.Rows.CountAsync()).Should().Be(0);
        (await fixture.Context.Journal.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("acquire")]
    [InlineData("save")]
    [InlineData("begin")]
    [InlineData("record")]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("dispose")]
    [InlineData("register")]
    public async Task ReentrantCallbacksFailFastInsteadOfDeadlocking(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context);
        await using var transaction = await work.BeginTransactionAsync();
        work.RegisterSaveParticipant(new object(), new Participant(async (_, token) =>
        {
            var nested = operation switch
            {
                "acquire" => work.AcquireAsync(token).AsTask(),
                "save" => work.SaveChangesAsync(token),
                "begin" => work.BeginTransactionAsync(cancellationToken: token).AsTask(),
                "record" => work.RecordChangesAsync(["nested"], token),
                "commit" => transaction.CommitAsync(token),
                "rollback" => transaction.RollbackAsync(token),
                "dispose" => work.DisposeAsync().AsTask(),
                _ => RegisterAsync()
            };
            await nested.WaitAsync(TimeSpan.FromSeconds(3), token);
        }));
        await work.Invoking(w => w.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot re-enter*");
        await AssertFailedWorkAsync(work, transaction);
        await transaction.RollbackAsync();

        Task RegisterAsync()
        {
            work.RegisterSaveParticipant(new object(), JournalParticipant());
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task InputTagAndPreSaveCancellationFailuresDoNotRunOrPoisonParticipants()
    {
        await using var fixture = await Fixture.CreateAsync();
        var participant = JournalParticipant();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "recoverable" });
        await work.Invoking(w => w.SaveChangesAsync([""])).Should().ThrowAsync<ArgumentException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await work.Invoking(w => w.SaveChangesAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        participant.Calls.Should().Be(0);
        (await work.SaveChangesAsync()).Should().Be(2);
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task CancellationAfterPreparationIsObservedEvenWhenTheParticipantIgnoresIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var participant = new Participant((context, token) =>
        {
            token.Should().Be(cancellation.Token);
            context.Journal.Add(new ParticipantJournal { Message = "canceled" });
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        });
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        await work.Invoking(w => w.SaveChangesAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        await AssertFailedWorkAsync(work, transaction);
        await transaction.RollbackAsync();
        (await fixture.Context.Journal.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvalidParticipantDependencyTagsFailBeforeSavingAndRequireRollback()
    {
        await using var fixture = await Fixture.CreateAsync();
        var participant = JournalParticipant();
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context,
            tagSelector: type => type == typeof(ParticipantJournal) ? [""] : ["rows"]);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "invalid tags" });
        await work.Invoking(w => w.SaveChangesAsync()).Should().ThrowAsync<ArgumentException>();
        await AssertFailedWorkAsync(work, transaction);
        await transaction.RollbackAsync();
        (await fixture.Context.Rows.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyedWorkSupportsRegistrationBeforeOrAfterBindingAndRejectsReentrantDisposal(bool beforeBinding)
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new KeyedProvider(fixture);
        var participant = JournalParticipant();
        await using var work = new KeyedRepositoryUnitOfWork<ParticipantContext, string>(provider);
        if (beforeBinding)
        {
            work.RegisterSaveParticipant(participant, participant);
        }

        await using var lease = await work.AcquireAsync("first");
        if (!beforeBinding)
        {
            work.RegisterSaveParticipant(participant, participant);
        }

        work.RegisterSaveParticipant(new object(), new Participant(async (_, _) =>
        {
            await work.Invoking(w => w.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot re-enter*");
        }));
        await using var transaction = await work.BeginTransactionAsync();
        lease.Context.Rows.Add(new ParticipantRow { Name = "keyed" });
        (await work.SaveChangesAsync()).Should().Be(2);
        work.RegisterSaveParticipant(participant, participant);
        work.Invoking(w => w.RegisterSaveParticipant(new object(), participant)).Should().Throw<InvalidOperationException>().WithMessage("*first save*");
        await work.Invoking(w => w.AcquireAsync("second").AsTask()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*switch context keys*");
        participant.Contexts.Should().ContainSingle().Which.Should().BeSameAs(lease.Context);
        provider.Keys.Should().Equal("first");
        await transaction.CommitAsync();
        await work.DisposeAsync();
        provider.Context!.DisposeCount.Should().Be(1);
        (await fixture.Context.Journal.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ObserverFailureIsStillACommittedOutcomeNotAParticipantFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        var participant = JournalParticipant();
        var observer = new Observer { Fail = true };
        await using var work = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context, [observer]);
        work.RegisterSaveParticipant(participant, participant);
        await using var transaction = await work.BeginTransactionAsync();
        fixture.Context.Rows.Add(new ParticipantRow { Name = "committed" });
        await work.SaveChangesAsync();
        await transaction.Invoking(t => t.CommitAsync()).Should().ThrowAsync<RepositoryCommitObserverException>();
        participant.Calls.Should().Be(1);
        (await fixture.Context.Rows.CountAsync()).Should().Be(1);
        (await fixture.Context.Journal.CountAsync()).Should().Be(1);
        await using var lease = await work.AcquireAsync(); // Already committed work is not marked as preparation-failed.
        await using var next = await work.BeginTransactionAsync();
        (await work.SaveChangesAsync()).Should().Be(0);
        await next.CommitAsync();
        observer.Tags.Should().ContainSingle();
    }

    [Fact]
    public async Task RegistrationValidatesArgumentsAndDisposedWorkWithoutAcquiringAContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new KeyedProvider(fixture);
        var participant = JournalParticipant();
        var work = new KeyedRepositoryUnitOfWork<ParticipantContext, string>(provider);
        work.Invoking(w => w.RegisterSaveParticipant(null!, participant)).Should().Throw<ArgumentNullException>();
        work.Invoking(w => w.RegisterSaveParticipant(new object(), null!)).Should().Throw<ArgumentNullException>();
        provider.Keys.Should().BeEmpty();
        await work.DisposeAsync();
        work.Invoking(w => w.RegisterSaveParticipant(new object(), participant)).Should().Throw<ObjectDisposedException>();
        var ordinary = new RepositoryUnitOfWork<ParticipantContext>(fixture.Context);
        ordinary.Invoking(w => w.RegisterSaveParticipant(null!, participant)).Should().Throw<ArgumentNullException>();
        ordinary.Invoking(w => w.RegisterSaveParticipant(new object(), null!)).Should().Throw<ArgumentNullException>();
        await ordinary.DisposeAsync();
        ordinary.Invoking(w => w.RegisterSaveParticipant(new object(), participant)).Should().Throw<ObjectDisposedException>();
        fixture.Context.DisposeCount.Should().Be(0);
    }

    private static async Task AssertFailedWorkAsync(RepositoryUnitOfWork<ParticipantContext> work, IRepositoryTransaction transaction)
    {
        await work.Invoking(w => w.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*participant save failed*");
        await work.Invoking(w => w.AcquireAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*participant save failed*");
        await work.Invoking(w => w.RecordChangesAsync(["cannot commit"])).Should().ThrowAsync<InvalidOperationException>().WithMessage("*participant save failed*");
        await transaction.Invoking(t => t.CommitAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*participant save failed*");
    }

    private static Participant JournalParticipant() => new((context, _) =>
    {
        var count = context.ChangeTracker.Entries<ParticipantRow>().Count(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        if (count > 0)
        {
            context.Journal.Add(new ParticipantJournal { Message = "save " + (context.ChangeTracker.Entries<ParticipantJournal>().Count() + 1) });
        }

        return ValueTask.CompletedTask;
    });

    private sealed record Identity(int Value);

    private sealed class Participant(Func<ParticipantContext, CancellationToken, ValueTask> callback) : IRepositorySaveParticipant<ParticipantContext>
    {
        public int Calls { get; private set; }
        public List<ParticipantContext> Contexts { get; } = [];
        public List<bool> HadTransaction { get; } = [];

        public ValueTask SavingAsync(ParticipantContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            Contexts.Add(context);
            HadTransaction.Add(context.Database.CurrentTransaction is not null);
            return callback(context, cancellationToken);
        }
    }

    private sealed class Observer : IRepositoryCommitObserver
    {
        public List<IReadOnlyCollection<string>> Tags { get; } = [];
        public bool Fail { get; init; }

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Tags.Add(tags);
            return Fail ? Task.FromException(new InvalidOperationException("observer failed")) : Task.CompletedTask;
        }
    }

    private sealed class KeyedProvider(Fixture fixture) : IKeyedDbContextLeaseProvider<ParticipantContext, string>
    {
        public List<string> Keys { get; } = [];
        public ParticipantContext? Context { get; private set; }

        public ValueTask<DbContextLease<ParticipantContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            Context = fixture.CreateContext();
            return ValueTask.FromResult(DbContextLease.Owned(Context, "scope:" + key));
        }
    }

    private sealed class Fixture(SqliteConnection connection, ParticipantContext context) : IAsyncDisposable
    {
        public ParticipantContext Context { get; } = context;
        public ParticipantContext CreateContext() => new(new DbContextOptionsBuilder<ParticipantContext>().UseSqlite(connection).Options);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ParticipantContext(new DbContextOptionsBuilder<ParticipantContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class ParticipantContext(DbContextOptions<ParticipantContext> options) : DbContext(options)
    {
        public DbSet<ParticipantRow> Rows => Set<ParticipantRow>();
        public DbSet<ParticipantJournal> Journal => Set<ParticipantJournal>();
        public int DisposeCount { get; private set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<ParticipantJournal>().HasIndex(row => row.Message).IsUnique();

        public override ValueTask DisposeAsync()
        {
            DisposeCount++;
            return base.DisposeAsync();
        }
    }

    private sealed class ParticipantRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ParticipantJournal
    {
        public int Id { get; set; }
        public string Message { get; set; } = "";
    }
}