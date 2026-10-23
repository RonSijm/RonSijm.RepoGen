using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>
/// Holds one context lease for a unit of work. Register the same scoped instance as the lease
/// provider for every participating repository. Like DbContext, it is not for concurrent operations.
/// </summary>
public sealed class RepositoryUnitOfWork<TContext> : IRepositoryUnitOfWork, IRepositoryCommitSource, IDbContextLeaseProvider<TContext>, IRepositorySaveParticipantSource<TContext>
    where TContext : DbContext
{
    private readonly IDbContextLeaseProvider<TContext> provider;
    private readonly IReadOnlyList<IRepositoryCommitObserver> observers;
    private readonly Func<Type, IEnumerable<string>> tagSelector;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> pendingTags = new(StringComparer.Ordinal);
    private readonly Dictionary<object, IRepositoryCommitObserver> registeredObservers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, IRepositorySaveParticipant<TContext>> saveParticipants = new(ReferenceEqualityComparer.Instance);
    private DbContextLease<TContext>? lease;
    private Transaction? transaction;
    private bool disposed;
    private bool rolledBack;
    private bool saveParticipantsFrozen;
    private bool executingParticipantSave;
    private bool participantSaveFailed;

    public RepositoryUnitOfWork(
        IDbContextLeaseProvider<TContext> provider,
        IEnumerable<IRepositoryCommitObserver>? observers = null,
        Func<Type, IEnumerable<string>>? tagSelector = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.provider = provider;
        this.observers = observers?.ToArray() ?? [];
        if (this.observers.Any(static observer => observer is null))
        {
            throw new ArgumentException("Commit observers cannot contain null.", nameof(observers));
        }

        this.tagSelector = tagSelector ?? (static type => [type.FullName ?? type.Name]);
    }

    public RepositoryUnitOfWork(
        TContext context,
        IEnumerable<IRepositoryCommitObserver>? observers = null,
        Func<Type, IEnumerable<string>>? tagSelector = null)
        : this(new ScopedDbContextLeaseProvider<TContext>(context), observers, tagSelector) { }

    public async ValueTask<DbContextLease<TContext>> AcquireAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotInParticipantSave();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return DbContextLease.Borrowed(await GetContextAsync(cancellationToken).ConfigureAwait(false), lease!.ScopeKey);
        }
        finally
        {
            gate.Release();
        }
    }

    public void RegisterCommitObserver(object identity, IRepositoryCommitObserver observer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(observer);
        registeredObservers.TryAdd(identity, observer);
    }

    public void RegisterSaveParticipant(object identity, IRepositorySaveParticipant<TContext> participant)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(participant);
        EnsureNotInParticipantSave();
        if (saveParticipants.ContainsKey(identity))
        {
            return;
        }

        if (saveParticipantsFrozen)
        {
            throw new InvalidOperationException("Register save participants before the unit of work's first save.");
        }

        saveParticipants.Add(identity, participant);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => SaveChangesAsync([], cancellationToken);

    public async Task<int> SaveChangesAsync(IEnumerable<string> additionalTags, CancellationToken cancellationToken = default)
    {
        EnsureNotInParticipantSave();
        ArgumentNullException.ThrowIfNull(additionalTags);
        var extraTags = additionalTags.ToArray();
        ValidateTags(extraTags);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
            if (saveParticipants.Count > 0 && transaction is null)
            {
                throw new InvalidOperationException("Save participants require an explicit transaction owned by this unit of work. Begin a transaction before saving.");
            }

            saveParticipantsFrozen = true;
            executingParticipantSave = saveParticipants.Count > 0;
            try
            {
                foreach (var participant in saveParticipants.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await participant.SavingAsync(context, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var changedTags = context.ChangeTracker.Entries()
                    .Where(static entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                    .SelectMany(entry => tagSelector(entry.Metadata.ClrType))
                    .ToArray();
                ValidateTags(changedTags);
                var count = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (count > 0)
                {
                    AddTags(changedTags);
                    AddTags(extraTags);
                }

                if (transaction is null)
                {
                    await NotifyCommitAsync(cancellationToken).ConfigureAwait(false);
                }

                return count;
            }
            catch when (executingParticipantSave)
            {
                // Participant-owned tracked changes cannot safely be replayed on this context.
                // Keep the native transaction alive only so the caller can roll it back/dispose it.
                participantSaveFailed = true;
                throw;
            }
            finally
            {
                executingParticipantSave = false;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecordChangesAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        EnsureNotInParticipantSave();
        ArgumentNullException.ThrowIfNull(tags);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await GetContextAsync(cancellationToken).ConfigureAwait(false);
            AddTags(tags);
            if (transaction is null)
            {
                await NotifyCommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IRepositoryTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default)
    {
        EnsureNotInParticipantSave();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = await GetContextAsync(cancellationToken).ConfigureAwait(false);
            if (transaction is not null)
            {
                throw new InvalidOperationException("This unit of work already has an active transaction.");
            }

            transaction = new Transaction(this,
                await context.Database.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false));
            return transaction;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        EnsureNotInParticipantSave();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                if (transaction is not null)
                {
                    await transaction.Native.DisposeAsync().ConfigureAwait(false);
                    transaction.Completed = true;
                    transaction = null;
                }
            }
            finally
            {
                pendingTags.Clear();
                if (lease is not null)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<TContext> GetContextAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (rolledBack)
        {
            throw new InvalidOperationException("A rolled-back unit of work cannot be reused. Create a new unit of work.");
        }

        ThrowIfParticipantSaveFailed();

        if (System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Use the unit of work's explicit transaction so commit observers have a known commit boundary.");
        }

        lease ??= await provider.AcquireAsync(cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("The context lease provider returned null.");
        var context = lease.Context;
        if (context.Database.CurrentTransaction is not null &&
            !ReferenceEquals(context.Database.CurrentTransaction, transaction?.Native))
        {
            throw new InvalidOperationException("The context has a transaction not owned by this unit of work.");
        }

        return context;
    }

    internal void EnsureNotInParticipantSave()
    {
        if (executingParticipantSave)
        {
            throw new InvalidOperationException("A save participant cannot re-enter its unit of work. Use the supplied context to stage changes; do not save or complete transactions from the callback.");
        }
    }

    private void ThrowIfParticipantSaveFailed()
    {
        if (participantSaveFailed)
        {
            throw new InvalidOperationException("A participant save failed. Roll back or dispose this unit of work and use a fresh context before retrying.");
        }
    }

    private void AddTags(IEnumerable<string> tags)
    {
        var values = tags.ToArray();
        ValidateTags(values);
        foreach (var tag in values)
        {
            pendingTags.Add(tag);
        }
    }

    private static void ValidateTags(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        }
    }

    private async Task NotifyCommitAsync(CancellationToken cancellationToken)
    {
        if (pendingTags.Count == 0)
        {
            return;
        }

        var tags = Array.AsReadOnly(pendingTags.Order(StringComparer.Ordinal).ToArray());
        pendingTags.Clear();
        var failures = new List<Exception>();
        foreach (var observer in observers.Concat(registeredObservers.Values))
        {
            try
            {
                await observer.CommittedAsync(tags, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new RepositoryCommitObserverException(failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }
    }

    private async Task CompleteAsync(Transaction current, bool commit, CancellationToken cancellationToken)
    {
        EnsureNotInParticipantSave();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current.Completed || !ReferenceEquals(transaction, current))
            {
                throw new InvalidOperationException("The transaction has already completed.");
            }

            if (commit)
            {
                ThrowIfParticipantSaveFailed();
            }

            Exception? cleanupFailure = null;
            try
            {
                if (commit)
                {
                    await current.Native.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await current.Native.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    rolledBack = true;
                }
            }
            catch
            {
                rolledBack = true;
                throw;
            }
            finally
            {
                current.Completed = true;
                transaction = null;
                if (rolledBack)
                {
                    pendingTags.Clear();
                }

                try
                {
                    await current.Native.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (commit && !rolledBack && exception is not OutOfMemoryException and not StackOverflowException)
                {
                    cleanupFailure = exception;
                }
            }

            if (commit)
            {
                try
                {
                    await NotifyCommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (RepositoryCommitObserverException exception) when (cleanupFailure is not null)
                {
                    throw new RepositoryCommitObserverException(new AggregateException(exception, cleanupFailure));
                }

                if (cleanupFailure is not null)
                {
                    throw new RepositoryCommitCleanupException(cleanupFailure);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class Transaction : IRepositoryTransaction
    {
        private readonly RepositoryUnitOfWork<TContext> owner;

        public Transaction(RepositoryUnitOfWork<TContext> owner, IDbContextTransaction native)
        {
            this.owner = owner;
            Native = native;
        }

        public IDbContextTransaction Native { get; }
        public bool Completed { get; set; }

        public Task CommitAsync(CancellationToken cancellationToken = default) => owner.CompleteAsync(this, true, cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default) => owner.CompleteAsync(this, false, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (!Completed)
            {
                await RollbackAsync().ConfigureAwait(false);
            }
        }
    }
}