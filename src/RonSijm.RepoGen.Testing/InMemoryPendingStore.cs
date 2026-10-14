namespace RonSijm.RepoGen.Testing;

internal sealed class InMemoryPendingStore<TEntity, TKey>(InMemoryWriteStore<TEntity, TKey> store) : IInMemoryPendingStore
    where TEntity : class where TKey : notnull
{
    internal Dictionary<TKey, InMemoryPendingEntry<TEntity>> Entries { get; private set; } = new(store.KeyComparer);
    internal void ReplaceEntries(Dictionary<TKey, InMemoryPendingEntry<TEntity>> entries) => Entries = entries;
    public int Count => Entries.Count;
    public void Clear() => Entries.Clear();
    internal Action<TEntity, IReadOnlyList<TEntity>>? GenerateOnCreate { get; set; }
    internal Action<TEntity, IReadOnlyList<TEntity>>? GenerateOnUpdate { get; set; }

    internal void PrepareCreate(TEntity row, IEnumerable<TEntity>? additionalRows = null)
    {
        if (GenerateOnCreate is null) return;
        var reserved = store.CopyWriteRows().Values.Concat(Entries.Values.Select(entry => store.Detach(entry.Current)))
            .Concat((additionalRows ?? []).Select(store.Detach)).ToArray();
        GenerateOnCreate(row, Array.AsReadOnly(reserved));
    }

    public InMemoryPreparedSave PrepareSave(CancellationToken cancellationToken)
    {
        var rows = store.CopyWriteRows();
        var count = 0;
        Dictionary<object, object>? generated = GenerateOnUpdate is null ? null : new(ReferenceEqualityComparer.Instance);
        foreach (var (key, entry) in Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Original is null)
            {
                if (!rows.TryAdd(key, store.Detach(entry.Current))) throw new InvalidOperationException("Duplicate key in in-memory fixture store: " + store.Name);
                count++;
                continue;
            }
            if (!entry.Deleted && !entry.ForceWrite && entry.SameValues(entry.Original, entry.Current)) continue;
            if (!rows.TryGetValue(key, out var current) || !entry.SameTokens(entry.Original, current))
                throw new InMemoryWriteConflictException(store.Name, entry);
            if (entry.Deleted) rows.Remove(key);
            else
            {
                IReadOnlyCollection<string> assigned = entry.Assigned;
                if (!entry.ForceWrite && entry.SameAssignedValue is { } sameAssigned)
                    assigned = entry.Assigned.Where(name => !sameAssigned(entry.Original, entry.Current, name)).ToArray();
                entry.Merge(current, entry.Current, assigned);
                if (GenerateOnUpdate is not null)
                {
                    GenerateOnUpdate(current, Array.AsReadOnly(rows.Values.Select(store.Detach).ToArray()));
                    generated!.Add(entry, store.Detach(current));
                }
                var copy = store.Detach(current);
                if (!store.KeyComparer.Equals(key, store.KeyOf(copy))) throw new InvalidOperationException("A simulated patch changed a fixture key: " + store.Name);
                rows[key] = copy;
            }
            count++;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var commit = store.PrepareCommitRows(rows);
        return new(count, () => { commit(); Entries.Clear(); }, generated);
    }
}
