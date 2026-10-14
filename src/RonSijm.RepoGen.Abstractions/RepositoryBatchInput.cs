namespace RonSijm.RepoGen.Abstractions;

/// <summary>A bounded, ordered snapshot of unique keys and items, independent of a persistence provider.</summary>
/// <remarks>Enumeration happens once. Keys must be immutable; mutable item contents remain caller-owned until execution.</remarks>
public sealed class RepositoryBatchInput<TItem, TKey> where TKey : notnull
{
    internal RepositoryBatchInput(List<KeyValuePair<TKey, TItem>> items)
    {
        Items = items.AsReadOnly();
        Keys = Array.AsReadOnly(items.Select(static item => item.Key).ToArray());
    }

    public IReadOnlyList<KeyValuePair<TKey, TItem>> Items { get; }
    public IReadOnlyList<TKey> Keys { get; }

}
