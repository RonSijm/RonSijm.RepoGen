namespace RonSijm.RepoGen.Testing;

internal sealed class InMemoryMappedWriteStore<TData, TEntity, TKey>(InMemoryStore<TData, TKey> store,
    Func<TData, TEntity> read, Func<TEntity, TData?, TData> write, Func<TEntity, TEntity> copy,
    Func<TEntity, TKey> key, Func<TEntity, TEntity, bool> sameValues) : InMemoryWriteStore<TEntity, TKey>
    where TData : class where TEntity : class where TKey : notnull
{
    public override string Name => store.Name;
    internal override InMemoryDataContext Context => store.Context;
    internal override object Identity => store;
    internal override IEqualityComparer<TKey> KeyComparer => store.KeyComparer;
    internal override TKey KeyOf(TEntity row) => key(row);

    internal override TEntity Detach(TEntity row)
    {
        var originalKey = key(row);
        var detached = copy(row) ?? throw Invalid("The scalar row copy returned null.");
        if (ReferenceEquals(row, detached)) throw Invalid("The scalar row copy must return a detached root.");
        if (originalKey is null || !KeyComparer.Equals(originalKey, key(detached))) throw Invalid("The scalar row copy changed a key.");
        if (!sameValues(row, detached)) throw Invalid("The scalar row copy changed mapped values.");
        return detached;
    }

    private TEntity Read(TKey expectedKey, TData data)
    {
        var row = Detach(read(data) ?? throw Invalid("The scalar read map returned null."));
        if (!KeyComparer.Equals(expectedKey, key(row))) throw Invalid("The scalar read map changed a storage key.");
        return row;
    }

    internal override Dictionary<TKey, TEntity> CopyWriteRows() => store.CopyWriteRows().ToDictionary(
        pair => pair.Key, pair => Read(pair.Key, pair.Value), KeyComparer);

    internal override Action PrepareCommitRows(Dictionary<TKey, TEntity> prepared)
    {
        // Encoding and validating every changed row happens before ANY participating store commits.
        // Keep unmodified arrangement-only data intact; the explicit writer receives saved data for changes.
        var originals = store.CopyWriteRows();
        var data = new Dictionary<TKey, TData>(KeyComparer);
        foreach (var (expectedKey, row) in prepared)
        {
            if (!KeyComparer.Equals(expectedKey, key(row))) throw Invalid("A simulated write changed a storage key.");
            originals.TryGetValue(expectedKey, out var original);
            if (original is not null && sameValues(Read(expectedKey, original), row)) { data.Add(expectedKey, original); continue; }
            var encoded = write(Detach(row), original) ?? throw Invalid("The public data write map returned null.");
            encoded = store.Detach(encoded);
            if (!KeyComparer.Equals(expectedKey, store.KeyOf(encoded))) throw Invalid("The public data write map changed a storage key.");
            if (!sameValues(row, Read(expectedKey, encoded))) throw Invalid("The public data write map did not round-trip all mapped scalar values.");
            data.Add(expectedKey, encoded);
        }
        return store.PrepareCommitRows(data);
    }

    private InvalidOperationException Invalid(string reason) => new("Invalid mapped fixture write for '" + Name + "': " + reason);
}
