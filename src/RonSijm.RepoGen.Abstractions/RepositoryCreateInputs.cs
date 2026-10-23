namespace RonSijm.RepoGen.Abstractions;

/// <summary>Snapshots a bounded create request once, before context/fixture acquisition.</summary>
/// <remarks>Item references are retained, not deep cloned. No artificial matching keys are assigned.</remarks>
public static class RepositoryCreateInputs
{
    public static IReadOnlyList<TItem> Prepare<TItem>(IEnumerable<TItem> items, int maximumItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (maximumItems < 1 || maximumItems == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = new List<TItem>();
        using var iterator = items.GetEnumerator();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!iterator.MoveNext()) break;
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Count == maximumItems) throw new ArgumentException("The create batch exceeds its configured item limit.", nameof(items));
            var item = iterator.Current;
            if (item is null) throw new ArgumentException("Create batch items must be non-null.", nameof(items));
            snapshot.Add(item);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot.AsReadOnly();
    }
}
