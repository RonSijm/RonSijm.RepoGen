using System.Data;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

/// <summary>Optional fixture-only pending writes, atomic shared saves and explicit rollback simulation.</summary>
/// <remarks>Read snapshots expose saved rows, not staged changes. All adapters/delegates must be pure.
/// Transactions share one fixture partition, provide no parallel isolation and accept Unspecified isolation only.
/// Generated values require explicit pure factories. No SQL constraints, cache observers or provider race guarantees are simulated.</remarks>
public partial class InMemoryWriteDataContext : InMemoryDataContext, IRepositoryUnitOfWork
{
    private readonly Dictionary<object, IInMemoryPendingStore> pending = new(ReferenceEqualityComparer.Instance);
    private InMemoryRepositoryTransaction? transaction;
    private bool disposed;

    public int PendingCount => Access(() => pending.Values.Sum(static store => store.Count));

    /// <summary>Discards staged changes without reverting already saved rows or changing baselines.</summary>
    public void DiscardChanges() => Access(ClearPending);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Run(() => Access(() =>
    {
        try { return SaveCore(cancellationToken); }
        catch (InMemoryWriteConflictException exception) { throw new RepositoryWriteConflictException(exception); }
    }), cancellationToken);

    public Task<int> SaveChangesAsync(IEnumerable<string> additionalTags, CancellationToken cancellationToken = default) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(additionalTags);
        foreach (var tag in additionalTags) ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return Access(() =>
        {
            try { return SaveCore(cancellationToken); }
            catch (InMemoryWriteConflictException exception) { throw new RepositoryWriteConflictException(exception); }
        });
    }, cancellationToken);

    public ValueTask<IRepositoryTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isolationLevel != IsolationLevel.Unspecified) throw new NotSupportedException("In-memory fixture transactions do not simulate database isolation levels.");
            return ValueTask.FromResult<IRepositoryTransaction>(Access(() =>
            {
                if (transaction is not null) throw new InvalidOperationException("A fixture transaction is already active.");
                if (pending.Values.Any(static store => store.Count != 0)) throw new InvalidOperationException("Save or discard pending fixture writes before starting a transaction.");
                var restore = CaptureRestoreRows();
                cancellationToken.ThrowIfCancellationRequested();
                transaction = new(this, restore);
                return transaction;
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return ValueTask.FromCanceled<IRepositoryTransaction>(cancellationToken); }
        catch (Exception exception) { return ValueTask.FromException<IRepositoryTransaction>(exception); }
    }

    public Task RecordChangesAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
        => Run<int>(() => throw new NotSupportedException("Native commit/cache observer notifications are not simulated by fixture transactions."), cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        Access(() =>
        {
            transaction?.Restore();
            transaction?.Complete();
            transaction = null;
            ClearPending();
            disposed = true;
        });
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected override void EnsureAccessAllowed() => ObjectDisposedException.ThrowIf(disposed, this);

    protected internal override void EnsureArrangementAllowed()
    {
        if (transaction is not null) throw new InvalidOperationException("Fixture arrangement is unavailable inside a simulated transaction; use designed commands.");
    }

    protected override void EnsureResetAllowed()
    {
        EnsureArrangementAllowed();
        if (pending.Values.Any(static store => store.Count != 0)) throw new InvalidOperationException("Save or discard pending writes before resetting fixture data/baselines.");
    }

    internal void CompleteTransaction(InMemoryRepositoryTransaction active, bool rollback, CancellationToken cancellationToken)
        => Access(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(transaction, active)) throw new InvalidOperationException("The fixture transaction is no longer active.");
            if (rollback) { active.Restore(); ClearPending(); }
            active.Complete();
            transaction = null;
        });

    private void ClearPending() { foreach (var store in pending.Values) store.Clear(); }

    private int SaveCore(CancellationToken cancellationToken, Dictionary<object, object>? generatedRows = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = pending.Values.Where(static store => store.Count != 0).Select(store => store.PrepareSave(cancellationToken)).ToArray();
        var count = checked(prepared.Sum(static change => change.AffectedRows));
        if (generatedRows is not null)
            foreach (var change in prepared)
                if (change.GeneratedRows is { } values)
                    foreach (var pair in values) generatedRows.Add(pair.Key, pair.Value);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var change in prepared) change.Commit();
        return count;
    }

    private InMemoryPendingStore<TEntity, TKey> Pending<TEntity, TKey>(InMemoryWriteStore<TEntity, TKey> store) where TEntity : class where TKey : notnull
    {
        if (!ReferenceEquals(store.Context, this)) throw new ArgumentException("The command store belongs to a different fixture context.", nameof(store));
        if (mappedWriters.TryGetValue(store.Identity, out var mapped) && !ReferenceEquals(mapped, store))
            throw new InvalidOperationException("Use the configured mapped write view instead of writing public arrangement rows directly: " + store.Name);
        if (!pending.TryGetValue(store.Identity, out var state)) pending.Add(store.Identity, state = new InMemoryPendingStore<TEntity, TKey>(store));
        return (InMemoryPendingStore<TEntity, TKey>)state;
    }

    private static Task<TResult> Run<TResult>(Func<TResult> action, CancellationToken cancellationToken)
    {
        try { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(action()); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Task.FromCanceled<TResult>(cancellationToken); }
        catch (Exception exception) { return Task.FromException<TResult>(exception); }
    }
}
