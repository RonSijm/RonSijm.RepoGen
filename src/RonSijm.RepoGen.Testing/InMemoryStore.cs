namespace RonSijm.RepoGen.Testing;

/// <summary>Keyed fixture arrangement and detached inspection with explicit key/copy adapters.</summary>
/// <remarks>Methods arrange test data immediately. They do not implement designer writes, validation, visibility or persistence.
/// Collection order is not a repository sort contract. Copies of nested mutable data and immutable keys are adapter responsibilities.</remarks>
public sealed class InMemoryStore<TEntity, TKey> : InMemoryWriteStore<TEntity, TKey>, IInMemoryStoreState where TEntity : class where TKey : notnull
{
    private readonly InMemoryDataContext context;
    private readonly Func<TEntity, TKey> keySelector;
    private readonly Func<TEntity, TEntity> copy;
    private readonly IEqualityComparer<TKey> comparer;
    private Dictionary<TKey, TEntity> rows;
    private Dictionary<TKey, TEntity> baseline;

    internal InMemoryStore(InMemoryDataContext context, string name, Func<TEntity, TKey> keySelector,
        Func<TEntity, TEntity> copy, IEqualityComparer<TKey> comparer)
    {
        this.context = context;
        Name = name;
        this.keySelector = keySelector;
        this.copy = copy;
        this.comparer = comparer;
        rows = new(comparer);
        baseline = new(comparer);
    }

    public override string Name { get; }
    public int Count => context.Access(() => rows.Count);

    /// <summary>Returns a detached copy, or null when no key exists.</summary>
    public TEntity? Find(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return context.Access(() => rows.TryGetValue(key, out var row) ? CopyStoredRow(key, row).Row : null);
    }

    /// <summary>Returns detached rows. Editing the collection/items does not change this store when the adapter deep-copies them.</summary>
    public IReadOnlyList<TEntity> Snapshot() => context.Access(CopySnapshot);

    public void Seed(TEntity row, InMemoryDuplicateKeyBehavior duplicateKeys = InMemoryDuplicateKeyBehavior.Throw)
    {
        ArgumentNullException.ThrowIfNull(row);
        SeedRange([row], duplicateKeys);
    }

    /// <summary>Enumerates once and adds detached rows atomically. Duplicate or adapter/enumeration failures leave this store unchanged.</summary>
    public void SeedRange(IEnumerable<TEntity> source, InMemoryDuplicateKeyBehavior duplicateKeys = InMemoryDuplicateKeyBehavior.Throw)
        => Arrange(source, duplicateKeys, replace: false);

    /// <summary>Atomically replaces current rows, leaving the baseline intact.</summary>
    public void ReplaceWith(IEnumerable<TEntity> source, InMemoryDuplicateKeyBehavior duplicateKeys = InMemoryDuplicateKeyBehavior.Throw)
        => Arrange(source, duplicateKeys, replace: true);

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return context.Access(() => { context.EnsureArrangementAllowed(); return rows.Remove(key); });
    }

    public void Clear() => context.Access(() => { context.EnsureArrangementAllowed(); rows.Clear(); });

    private void Arrange(IEnumerable<TEntity> source, InMemoryDuplicateKeyBehavior duplicates, bool replace)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(duplicates)) throw new ArgumentOutOfRangeException(nameof(duplicates));
        // Enumeration also runs under the non-reentrant context guard: an input iterator cannot partly mutate the fixture.
        context.Access(() =>
        {
            context.EnsureArrangementAllowed();
            var prepared = replace ? new Dictionary<TKey, TEntity>(comparer) : new Dictionary<TKey, TEntity>(rows, comparer);
            foreach (var input in source)
            {
                ArgumentNullException.ThrowIfNull(input);
                var item = CopyRow(input);
                if (prepared.TryAdd(item.Key, item.Row)) continue;
                if (duplicates == InMemoryDuplicateKeyBehavior.Throw)
                    throw new InvalidOperationException("Duplicate key while arranging in-memory fixture store: " + Name);
                if (duplicates == InMemoryDuplicateKeyBehavior.Replace) prepared[item.Key] = item.Row;
            }

            rows = prepared;
        });
    }

    private (TKey Key, TEntity Row) CopyRow(TEntity row)
    {
        var key = keySelector(row);
        if (key is null) throw new InvalidOperationException("The fixture key adapter returned null: " + Name);
        var detached = copy(row) ?? throw new InvalidOperationException("The fixture copy adapter returned null: " + Name);
        if (ReferenceEquals(row, detached)) throw new InvalidOperationException("The fixture copy adapter must return a detached root: " + Name);
        var copiedKey = keySelector(detached);
        if (copiedKey is null || !comparer.Equals(key, copiedKey))
            throw new InvalidOperationException("The fixture copy adapter changed the row key: " + Name);
        return (copiedKey, detached);
    }

    private Dictionary<TKey, TEntity> CopyRows(Dictionary<TKey, TEntity> source)
    {
        var detached = new Dictionary<TKey, TEntity>(comparer);
        foreach (var pair in source)
        {
            var item = CopyStoredRow(pair.Key, pair.Value);
            detached.Add(item.Key, item.Row);
        }

        return detached;
    }

    private (TKey Key, TEntity Row) CopyStoredRow(TKey expectedKey, TEntity row)
    {
        var item = CopyRow(row);
        if (!comparer.Equals(expectedKey, item.Key)) throw new InvalidOperationException("A fixture row key changed after arrangement: " + Name);
        return item;
    }

    Action IInMemoryStoreState.PrepareCaptureBaseline()
    {
        var prepared = CopyRows(rows);
        return () => baseline = prepared;
    }

    Action IInMemoryStoreState.PrepareRestoreBaseline()
    {
        var prepared = CopyRows(baseline);
        return () => rows = prepared;
    }

    void IInMemoryStoreState.ClearRows() => rows.Clear();

    (Type RowType, object Rows) IInMemoryStoreState.SnapshotRows() => (typeof(TEntity), CopySnapshot());

    private IReadOnlyList<TEntity> CopySnapshot() => Array.AsReadOnly(rows.Select(pair => CopyStoredRow(pair.Key, pair.Value).Row).ToArray());

    Action IInMemoryStoreState.PrepareRestoreCheckpoint()
    {
        var prepared = CopyRows(rows);
        return () => rows = prepared;
    }

    internal override InMemoryDataContext Context => context;
    internal override object Identity => this;
    internal override IEqualityComparer<TKey> KeyComparer => comparer;
    internal override TKey KeyOf(TEntity row) => keySelector(row);
    internal override TEntity Detach(TEntity row) => CopyRow(row).Row;
    internal override Dictionary<TKey, TEntity> CopyWriteRows() => CopyRows(rows);
    internal override Action PrepareCommitRows(Dictionary<TKey, TEntity> prepared) => () => rows = prepared;
}
