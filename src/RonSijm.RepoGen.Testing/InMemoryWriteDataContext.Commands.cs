using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    public Task<CommandResult<TResult>> CreateAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity> factory, Func<TEntity, TResult> result, bool save, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
        => Execute(store, null, factory, null, static _ => true, static (_, _) => true, static (_, _) => true,
            static (_, _, _) => { }, result, false, save, false, cancellationToken);

    public Task<CommandResult<TResult>> PatchAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Func<TEntity, IReadOnlyList<string>> patch, Func<TEntity, bool> expected,
        Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<TEntity, TResult> result,
        bool save, bool forceWrite = false, CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
        => Execute(store, predicate, null, patch, expected, sameValues, sameTokens, merge, result, false, save, forceWrite, cancellationToken);

    /// <summary>Uses explicit per-field equality without applying equivalent assignments unless the write is forced.</summary>
    public Task<CommandResult<TResult>> PatchAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Func<TEntity, IReadOnlyList<string>> patch, Func<TEntity, bool> expected,
        Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<TEntity, TResult> result,
        bool save, bool forceWrite, Func<TEntity, TEntity, string, bool>? sameAssignedValue,
        CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
        => Execute(store, predicate, null, patch, expected, sameValues, sameTokens, merge, result, false, save, forceWrite, cancellationToken,
            sameAssignedValue: sameAssignedValue);

    public Task<CommandResult<TResult>> DeleteAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Func<TEntity, bool> expected, Func<TEntity, TEntity, bool> sameTokens,
        Func<TEntity, TResult> result, bool save, CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
        => Execute(store, predicate, null, null, expected, static (_, _) => true, sameTokens, static (_, _, _) => { }, result, true, save, true, cancellationToken);

    private Task<CommandResult<TResult>> Execute<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool>? predicate, Func<TEntity>? factory, Func<TEntity, IReadOnlyList<string>>? patch,
        Func<TEntity, bool> expected, Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<TEntity, TResult> result,
        bool delete, bool save, bool forceWrite, CancellationToken cancellationToken,
        IReadOnlyList<InMemoryCollectionWrite<TEntity>>? collections = null,
        Func<TEntity, TEntity, string, bool>? sameAssignedValue = null) where TEntity : class where TKey : notnull
        => Run(() =>
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(sameValues);
            ArgumentNullException.ThrowIfNull(sameTokens);
            ArgumentNullException.ThrowIfNull(merge);
            ArgumentNullException.ThrowIfNull(result);
            var prepared = Access(() => Prepare());
            if (prepared.Row is null) return new CommandResult<TResult>(prepared.Status, prepared.Count);
            try { return new CommandResult<TResult>(CommandStatus.Applied, prepared.Count, result(prepared.Row), hasValue: true); }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            { throw new RepositorySavedResultException(exception); }

            (CommandStatus Status, int Count, TEntity? Row) Prepare()
            {
                var state = Pending(store);
                var row = factory is null
                    ? store.CopyWriteRows().Values.SingleOrDefault(predicate ?? throw new ArgumentNullException(nameof(predicate)))
                    : factory() ?? throw new InvalidOperationException("The create mapping returned null.");
                cancellationToken.ThrowIfCancellationRequested();
                if (row is null) return (CommandStatus.NotFound, 0, null);
                row = store.Detach(row);
                if (factory is not null) { state.PrepareCreate(row); row = store.Detach(row); }
                var key = store.KeyOf(row);
                if (key is null) throw new InvalidOperationException("The fixture key adapter returned null: " + store.Name);
                state.Entries.TryGetValue(key, out var previous);
                if (factory is not null && (previous is not null || store.CopyWriteRows().ContainsKey(key)))
                    throw new InvalidOperationException("Duplicate key in in-memory fixture store: " + store.Name);
                if (!delete && previous?.Deleted == true) throw new InvalidOperationException("Cannot patch a row already staged for deletion.");
                var entry = previous?.Clone(store.Detach) ?? new InMemoryPendingEntry<TEntity>
                {
                    Current = row, Original = factory is null ? store.Detach(row) : null,
                    SameValues = sameValues, SameTokens = sameTokens, Merge = merge, SameAssignedValue = sameAssignedValue
                };
                if (factory is null && !expected(entry.Original!)) return (CommandStatus.Conflict, 0, null);
                var assignedAny = factory is not null || delete;
                if (factory is null && !delete)
                {
                    var assigned = (patch ?? throw new ArgumentNullException(nameof(patch)))(entry.Current);
                    assignedAny = assigned.Count != 0;
                    entry.Assigned.UnionWith(assigned);
                    if (!store.KeyComparer.Equals(key, store.KeyOf(entry.Current))) throw new InvalidOperationException("A simulated patch changed a fixture key: " + store.Name);
                }
                entry.Deleted = delete;
                entry.ForceWrite |= forceWrite;
                // Setter values may belong to mutable request objects. Own them before staging, not just at save.
                entry.Current = store.Detach(entry.Current);
                var childChanges = new List<InMemoryCollectionChanges>();
                try
                {
                    foreach (var collection in collections ?? [])
                    {
                        if (collection is null || !ReferenceEquals(collection.Context, this))
                            throw new ArgumentException("The collection write belongs to a different fixture context.", nameof(collections));
                        var changes = collection.Apply(entry.Current, cancellationToken);
                        childChanges.Add(changes);
                        assignedAny |= changes.Assigned;
                    }
                    if (!assignedAny) return (CommandStatus.NoChanges, 0, null);
                    cancellationToken.ThrowIfCancellationRequested();
                    state.Entries[key] = entry;
                    if (!save) return (CommandStatus.Staged, 0, null);
                    var resultRow = store.Detach(entry.Current);
                    var generated = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
                    var count = SaveCore(cancellationToken, generated);
                    return (CommandStatus.Applied, count, generated.TryGetValue(entry, out var value) ? (TEntity)value : resultRow);
                }
                catch (Exception exception)
                {
                    for (var index = childChanges.Count - 1; index >= 0; index--) childChanges[index].Restore();
                    if (previous is null) state.Entries.Remove(key); else state.Entries[key] = previous;
                    if (exception is InMemoryCollectionConflictException) return (CommandStatus.Conflict, 0, null);
                    if (exception is InMemoryWriteConflictException conflict)
                    {
                        if (ReferenceEquals(conflict.Entry, entry) || childChanges.Any(change => change.Entries.Any(child => ReferenceEquals(child, conflict.Entry))))
                            return (CommandStatus.Conflict, 0, null);
                        throw new RepositoryWriteConflictException(conflict);
                    }
                    throw;
                }
            }
        }, cancellationToken);
}
