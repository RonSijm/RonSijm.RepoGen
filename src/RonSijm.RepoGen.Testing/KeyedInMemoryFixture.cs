namespace RonSijm.RepoGen.Testing;

/// <summary>Isolated context partitions whose data/store identities survive baseline resets.</summary>
/// <remarks>Keys must be immutable with stable equality. Partition creation is synchronized; each partition owns its own data lock.
/// Capture/restore operate on a selected partition, not across all context keys.</remarks>
public sealed class KeyedInMemoryFixture<TKey, TData> where TKey : notnull where TData : InMemoryDataContext, new()
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, TData> partitions;

    public KeyedInMemoryFixture(IEqualityComparer<TKey>? keyComparer = null) => partitions = new(keyComparer ?? EqualityComparer<TKey>.Default);

    public IReadOnlyList<TKey> Keys { get { lock (gate) return Array.AsReadOnly(partitions.Keys.ToArray()); } }

    /// <summary>Returns the same data object for equal keys, creating an initially empty partition when necessary.</summary>
    public TData For(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (gate)
        {
            if (!partitions.TryGetValue(key, out var data))
            {
                data = new();
                partitions.Add(key, data);
            }

            return data;
        }
    }
}
