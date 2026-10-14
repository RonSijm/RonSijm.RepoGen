namespace RonSijm.RepoGen.Testing;

/// <summary>Detached rows captured together from selected stores in one fixture context partition.</summary>
/// <remarks>This is an arrangement snapshot, not a database transaction or relationship-fixup graph.
/// Mutable nested values remain the copy adapters' responsibility.</remarks>
public sealed class InMemoryDataSnapshot
{
    private readonly Dictionary<string, (Type RowType, object Rows)> stores;

    internal InMemoryDataSnapshot(Dictionary<string, (Type RowType, object Rows)> stores)
    {
        this.stores = stores;
        StoreNames = Array.AsReadOnly(stores.Keys.ToArray());
    }

    public IReadOnlyList<string> StoreNames { get; }

    /// <summary>Returns a captured store's detached rows; repeated access returns the same snapshot collection.</summary>
    public IReadOnlyList<TEntity> GetRows<TEntity>(string storeName) where TEntity : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        if (!stores.TryGetValue(storeName, out var store))
            throw new KeyNotFoundException("The store was not included in this fixture snapshot: " + storeName);
        if (store.RowType != typeof(TEntity))
            throw new InvalidOperationException("The requested row type does not match the fixture snapshot store: " + storeName);
        return (IReadOnlyList<TEntity>)store.Rows;
    }
}
