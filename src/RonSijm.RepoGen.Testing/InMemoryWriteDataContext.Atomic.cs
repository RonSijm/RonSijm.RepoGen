using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Simulates immediate insert-if-absent over a declared conflict target in saved rows only.</summary>
    /// <remarks>No pending writes are flushed, refreshed or reserved. Factories and projections finish before
    /// commit. This is deterministic fixture behavior, not a simulation of SQL uniqueness, locks or races.</remarks>
    public Task<CommandResult<TResult>> ExecuteInsertIfAbsentAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity> create, Func<TEntity, TEntity, bool> sameConflict, Func<TEntity, TResult> projection,
        CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
        => Run(() => Access(() => ExecuteInsert(store, create, sameConflict, null, null, projection, null, cancellationToken)), cancellationToken);

    /// <summary>Simulates immediate insert, or a conditional update of one saved conflict-target row.</summary>
    /// <remarks>Visibility and expected versions constrain the update branch, not insertion or conflict detection.
    /// All setters read the original row. Saved input values/factories never replace the existing row key.
    /// Existing staging is retained, including later shared-save conflicts. No relational guarantees are implied.</remarks>
    public Task<CommandResult<TResult>> ExecuteInsertOrUpdateAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity> create, Func<TEntity, TEntity, bool> sameConflict, Func<TEntity, bool> predicate,
        Action<TEntity, TEntity> update, Func<TEntity, TResult> projection, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
        => ExecuteInsertOrUpdateAsync(store, create, sameConflict, predicate, update, projection, null, cancellationToken);

    /// <summary>Simulates an upsert whose updated result is read through an explicit model-filter predicate.</summary>
    /// <remarks>The result filter applies only after a successful existing-row update and its factories,
    /// never to insertion/conflict probing. A missing filtered result fails before fixture commit;
    /// a real provider inside a caller-owned transaction may require explicit rollback instead.</remarks>
    public Task<CommandResult<TResult>> ExecuteInsertOrUpdateAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity> create, Func<TEntity, TEntity, bool> sameConflict, Func<TEntity, bool> predicate,
        Action<TEntity, TEntity> update, Func<TEntity, TResult> projection, Func<TEntity, bool>? updatedResultFilter,
        CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(update);
        return Run(() => Access(() => ExecuteInsert(store, create, sameConflict, predicate, update, projection, updatedResultFilter, cancellationToken)), cancellationToken);
    }

    private CommandResult<TResult> ExecuteInsert<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity> create, Func<TEntity, TEntity, bool> sameConflict, Func<TEntity, bool>? predicate,
        Action<TEntity, TEntity>? update, Func<TEntity, TResult> projection, Func<TEntity, bool>? updatedResultFilter, CancellationToken cancellationToken,
        Func<TEntity, InMemoryDataSnapshot, TResult>? savedProjection = null, string[]? resultStoreNames = null)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(sameConflict);
        ArgumentNullException.ThrowIfNull(projection);
        var state = Pending(store);
        var input = store.Detach(create() ?? throw new InvalidOperationException("The fixture insert mapping returned null: " + store.Name));
        var rows = store.CopyWriteRows();
        var matches = new List<(TKey Key, TEntity Row)>();
        foreach (var (key, row) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sameConflict(store.Detach(row), store.Detach(input))) matches.Add((key, row));
            if (matches.Count > 1)
                throw new InvalidOperationException("Multiple saved rows match a declared unique fixture conflict target: " + store.Name);
        }
        cancellationToken.ThrowIfCancellationRequested();
        TEntity candidate;
        TKey candidateKey;
        CommandStatus status;
        if (matches.Count == 0)
        {
            candidate = store.Detach(input);
            // Native simulation intentionally excludes pending reservations, unlike tracked Create.
            state.GenerateOnCreate?.Invoke(candidate, Array.AsReadOnly(rows.Values.Select(store.Detach).ToArray()));
            if (!sameConflict(store.Detach(input), store.Detach(candidate)))
                throw new InvalidOperationException("A fixture insert factory changed the declared conflict target: " + store.Name);
            candidateKey = store.KeyOf(candidate);
            if (!rows.TryAdd(candidateKey, store.Detach(candidate)))
                throw new InvalidOperationException("Duplicate key in immediate fixture insertion: " + store.Name);
            status = CommandStatus.Inserted;
        }
        else
        {
            if (update is null) return new(CommandStatus.AlreadyExists);
            var (key, original) = matches[0];
            if (!predicate!(store.Detach(original)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new(CommandStatus.NotMatched);
            }
            candidate = store.Detach(original);
            update(store.Detach(original), candidate);
            CheckTarget(original, candidate, key);
            rows[key] = store.Detach(candidate);
            state.GenerateOnUpdate?.Invoke(candidate, Array.AsReadOnly(rows.Values.Select(store.Detach).ToArray()));
            CheckTarget(original, candidate, key);
            rows[key] = store.Detach(candidate);
            candidateKey = key;
            status = CommandStatus.Updated;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (status == CommandStatus.Updated && updatedResultFilter is not null)
        {
            var visible = updatedResultFilter(store.Detach(rows[candidateKey]));
            cancellationToken.ThrowIfCancellationRequested();
            if (!visible)
                throw new InvalidOperationException("The fixture atomic update's filtered result read no longer matches the saved candidate. No fixture rows were committed.");
        }
        Action commit;
        TResult result;
        if (savedProjection is not null)
        {
            var prepared = store.PrepareCommitSnapshot(rows);
            var snapshot = CaptureSnapshotCore(resultStoreNames!, store.Name, prepared.Snapshot);
            result = savedProjection(store.Detach(rows[candidateKey]), snapshot);
            commit = prepared.Commit;
        }
        else
        {
            commit = store.PrepareCommitRows(rows);
            result = projection(store.Detach(rows[candidateKey]));
        }
        cancellationToken.ThrowIfCancellationRequested();
        commit();
        return new(status, 1, result, hasValue: true);

        void CheckTarget(TEntity original, TEntity changed, TKey key)
        {
            if (!store.KeyComparer.Equals(key, store.KeyOf(changed)) || !sameConflict(store.Detach(original), store.Detach(changed)))
                throw new InvalidOperationException("A fixture upsert changed a row key or declared conflict target: " + store.Name);
        }
    }
}
