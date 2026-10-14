using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Immediately updates saved matching rows, without saving or refreshing pending writes.</summary>
    /// <remarks>Assign the candidate from the detached original, not from previous setter results.
    /// Explicit generated-value callbacks run before commit. This is fixture simulation, not native SQL.</remarks>
    public Task<int> ExecuteUpdateAsync<TEntity, TKey>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Action<TEntity, TEntity> update, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(update);
        return Run(() => Access(() => ExecuteImmediate<TEntity, TKey, int>(store, predicate, update, null, cancellationToken).Count), cancellationToken);
    }

    /// <summary>Immediately deletes saved matching rows, without saving or refreshing pending writes.</summary>
    public Task<int> ExecuteDeleteAsync<TEntity, TKey>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
        => Run(() => Access(() => ExecuteImmediate<TEntity, TKey, int>(store, predicate, null, null, cancellationToken).Count), cancellationToken);

    /// <summary>Simulates an immediate update returning detached post-update scalar values in no promised order.</summary>
    /// <remarks>All value factories, public write maps and projections complete before any rows commit.
    /// Failure/cancellation leaves saved and pending rows unchanged. SQL locking, constraints and triggers are not simulated.</remarks>
    public Task<MutationResult<TResult>> ExecuteUpdateReturningAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Action<TEntity, TEntity> update, Func<TEntity, TResult> projection,
        CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(projection);
        return Run(() => Access(() => new MutationResult<TResult>(ExecuteImmediate(store, predicate, update, projection, cancellationToken).Items)), cancellationToken);
    }

    /// <summary>Simulates an immediate delete returning detached deleted scalar values in no promised order.</summary>
    public Task<MutationResult<TResult>> ExecuteDeleteReturningAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Func<TEntity, TResult> projection, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Run(() => Access(() => new MutationResult<TResult>(ExecuteImmediate(store, predicate, null, projection, cancellationToken).Items)), cancellationToken);
    }

    private (int Count, IReadOnlyList<TResult> Items) ExecuteImmediate<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Action<TEntity, TEntity>? update, Func<TEntity, TResult>? projection, CancellationToken cancellationToken)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(predicate);
        // This checks store ownership/mapped-write routing, but never flushes or edits pending entries.
        var state = Pending(store);
        var rows = store.CopyWriteRows();
        var targets = new List<(TKey Key, TEntity Row)>();
        foreach (var (key, row) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate(store.Detach(row))) targets.Add((key, row));
        }
        if (targets.Count == 0) return (0, Array.Empty<TResult>());

        foreach (var (key, original) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update is null) { rows.Remove(key); continue; }
            var candidate = store.Detach(original);
            update(store.Detach(original), candidate);
            CheckKey(key, candidate);
            rows[key] = store.Detach(candidate);
        }
        if (update is not null && state.GenerateOnUpdate is { } generate)
            foreach (var (key, _) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = store.Detach(rows[key]);
                generate(candidate, Array.AsReadOnly(rows.Values.Select(store.Detach).ToArray()));
                CheckKey(key, candidate);
                rows[key] = store.Detach(candidate);
            }

        // Mapped encoders and returned-value construction can fail. Neither can leave a partial write.
        var commit = store.PrepareCommitRows(rows);
        var items = new List<TResult>();
        if (projection is not null)
            foreach (var (key, original) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(projection(store.Detach(update is null ? original : rows[key])));
            }
        cancellationToken.ThrowIfCancellationRequested();
        commit();
        return (targets.Count, items.AsReadOnly());

        void CheckKey(TKey key, TEntity candidate)
        {
            if (!store.KeyComparer.Equals(key, store.KeyOf(candidate)))
                throw new InvalidOperationException("An immediate simulated update changed a fixture key: " + store.Name);
        }
    }
}
