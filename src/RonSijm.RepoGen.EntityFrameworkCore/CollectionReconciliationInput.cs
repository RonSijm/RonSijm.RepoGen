using System.Collections.ObjectModel;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>A bounded snapshot of a configured collection input, prepared before context acquisition.</summary>
public sealed class CollectionReconciliationInput<TItem, TKey> where TKey : notnull
{
    internal CollectionReconciliationInput(Dictionary<TKey, TItem> items, int maximumItems)
    {
        Items = new ReadOnlyDictionary<TKey, TItem>(items);
        MaximumItems = maximumItems;
    }

    public IReadOnlyDictionary<TKey, TItem> Items { get; }
    public int MaximumItems { get; }

}
