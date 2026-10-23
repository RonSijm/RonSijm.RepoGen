namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    private readonly Dictionary<object, object> mappedWriters = new(ReferenceEqualityComparer.Instance);

    /// <summary>Registers one explicit scalar command view of a public arrangement store.</summary>
    /// <remarks>The writer receives saved data for updates and null for creates. Adapters must be pure,
    /// preserve keys and round-trip all scalar values. Encoding completes before a shared save commits.
    /// The scalar copier must deep-copy mutable values. No persistence entity is required.</remarks>
    public InMemoryWriteStore<TEntity, TKey> MapWriteStore<TData, TEntity, TKey>(InMemoryStore<TData, TKey> store,
        Func<TData, TEntity> read, Func<TEntity, TData?, TData> write, Func<TEntity, TEntity> copy,
        Func<TEntity, TKey> key, Func<TEntity, TEntity, bool> sameValues)
        where TData : class where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(sameValues);
        return Access(() =>
        {
            EnsureResetAllowed();
            if (!ReferenceEquals(store.Context, this)) throw new ArgumentException("The mapped store belongs to a different fixture context.", nameof(store));
            if (mappedWriters.ContainsKey(store) || pending.ContainsKey(store))
                throw new InvalidOperationException("A fixture store already has a write view or configured write state: " + store.Name);
            var mapped = new InMemoryMappedWriteStore<TData, TEntity, TKey>(store, read, write, copy, key, sameValues);
            mappedWriters.Add(store, mapped);
            return mapped;
        });
    }
}
