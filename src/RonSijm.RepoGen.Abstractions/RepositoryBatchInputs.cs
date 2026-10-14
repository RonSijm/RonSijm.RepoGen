namespace RonSijm.RepoGen.Abstractions;

/// <summary>Shared provider-neutral input validation for designed bounded batches.</summary>
public static class RepositoryBatchInputs
{
    public static RepositoryBatchInput<TItem, TKey> Prepare<TItem, TKey>(IEnumerable<TItem> items, Func<TItem, TKey> key,
        int maximumItems, CancellationToken cancellationToken = default) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumItems, int.MaxValue - 1);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = new List<KeyValuePair<TKey, TItem>>();
        var keys = new HashSet<TKey>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Count == maximumItems)
                throw new ArgumentException($"Batch input exceeds its limit of {maximumItems} items.", nameof(items));
            if (item is null) throw new ArgumentException("Batch input cannot contain null items.", nameof(items));
            var value = key(item);
            if (value is null || !keys.Add(value))
                throw new ArgumentException("Batch input keys must be non-null and unique.", nameof(items));
            snapshot.Add(new(value, item));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(snapshot);
    }
}
