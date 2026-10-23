namespace RonSijm.RepoGen.Testing;

/// <summary>Coordinates typed fixture stores and atomic baseline capture/restore within one context partition.</summary>
/// <remarks>Arrangement is immediate; this is not a unit of work or transaction simulator.
/// Key/copy adapters run under the context lock and must be pure, non-reentrant and independently deep-copy mutable data.</remarks>
public class InMemoryDataContext
{
    private readonly object gate = new();
    private readonly Dictionary<string, IInMemoryStoreState> stores = new(StringComparer.Ordinal);
    private bool accessing;

    /// <summary>Registers a store once. Keys must be immutable and the copier must return a new root with the same key.</summary>
    public InMemoryStore<TEntity, TKey> CreateStore<TEntity, TKey>(string name, Func<TEntity, TKey> keySelector,
        Func<TEntity, TEntity> copy, IEqualityComparer<TKey>? keyComparer = null) where TEntity : class where TKey : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(copy);
        return Access(() =>
        {
            EnsureArrangementAllowed();
            if (stores.ContainsKey(name)) throw new InvalidOperationException("An in-memory fixture store is already registered: " + name);
            var store = new InMemoryStore<TEntity, TKey>(this, name, keySelector, copy, keyComparer ?? EqualityComparer<TKey>.Default);
            stores.Add(name, store);
            return store;
        });
    }

    /// <summary>Captures detached baselines for every registered store, or changes none if any adapter fails.</summary>
    public void CaptureBaseline() => Access(() => { EnsureResetAllowed(); PrepareAndCommit(static store => store.PrepareCaptureBaseline()); });

    /// <summary>Restores detached baseline rows in every store, or changes none if any adapter fails. The initial baseline is empty.</summary>
    public void RestoreBaseline() => Access(() => { EnsureResetAllowed(); PrepareAndCommit(static store => store.PrepareRestoreBaseline()); });

    /// <summary>Clears arranged rows in all stores without changing baselines or replacing store instances.</summary>
    public void Clear() => Access(() =>
    {
        EnsureResetAllowed();
        foreach (var store in stores.Values) store.ClearRows();
    });

    /// <summary>Copies selected stores under one context lock. Unselected stores and baselines are untouched.</summary>
    /// <remarks>Names are validated before any copy adapter runs. An empty selection captures no stores.
    /// All rows are detached before the snapshot is returned; queries run outside the context lock.</remarks>
    public InMemoryDataSnapshot CaptureSnapshot(params string[] storeNames)
    {
        ArgumentNullException.ThrowIfNull(storeNames);
        // Own the selection before entering the lock; a caller cannot change which stores are copied mid-capture.
        var names = storeNames.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in names) ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Access(() => CaptureSnapshotCore(names));
    }

    // Called only under Access. The replacement is a detached, unpublished write-store snapshot.
    private protected InMemoryDataSnapshot CaptureSnapshotCore(string[] names, string? replacementName = null,
        (Type RowType, object Rows)? replacement = null)
    {
        foreach (var name in names)
        {
            if (!stores.ContainsKey(name))
            {
                throw new KeyNotFoundException("An in-memory fixture store is not registered: " + name);
            }
        }
        if (replacementName is not null && !names.Contains(replacementName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The atomic result snapshot must include its root store: " + replacementName);
        }
        var snapshot = new Dictionary<string, (Type RowType, object Rows)>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            snapshot.Add(name, name == replacementName ? replacement!.Value : stores[name].SnapshotRows());
        }
        return new InMemoryDataSnapshot(snapshot);
    }

    internal TResult Access<TResult>(Func<TResult> action)
    {
        lock (gate)
        {
            EnsureAccessAllowed();
            if (accessing) throw new InvalidOperationException("In-memory fixture key/copy adapters cannot re-enter their data context.");
            accessing = true;
            try { return action(); }
            finally { accessing = false; }
        }
    }

    internal void Access(Action action) => Access(() => { action(); return true; });

    protected virtual void EnsureAccessAllowed() { }

    protected internal virtual void EnsureArrangementAllowed() { }
    protected virtual void EnsureResetAllowed() => EnsureArrangementAllowed();

    internal Action CaptureRestoreRows()
    {
        var restores = stores.Values.Select(static store => store.PrepareRestoreCheckpoint()).ToArray();
        return () => { foreach (var restore in restores) restore(); };
    }

    private void PrepareAndCommit(Func<IInMemoryStoreState, Action> prepare)
    {
        // All adapters finish successfully before any store/baseline changes; commits only swap owned dictionaries.
        var changes = stores.Values.Select(prepare).ToArray();
        foreach (var commit in changes) commit();
    }
}
