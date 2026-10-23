namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Bounded, ordered item/key snapshots prepared before a generated command acquires a context.</summary>
public sealed class BatchCommandInput<TItem, TKey> where TKey : notnull
{
    internal BatchCommandInput(RonSijm.RepoGen.Abstractions.RepositoryBatchInput<TItem, TKey> input)
    {
        Items = input.Items;
        Keys = input.Keys;
    }

    public IReadOnlyList<KeyValuePair<TKey, TItem>> Items { get; }
    public IReadOnlyList<TKey> Keys { get; }
}
