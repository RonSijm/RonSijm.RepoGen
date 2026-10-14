namespace RonSijm.RepoGen.Testing;

/// <summary>Correlates a coordinated detached saved snapshot to batch inputs using complete structural keys.</summary>
public static class InMemorySavedBatchResults
{
    public static IReadOnlyList<TResult> Project<TEntity, TResult>(IReadOnlyList<TEntity> savedRows,
        IEnumerable<TEntity> snapshot, Func<TEntity, object?[]> key, Func<TEntity, TResult> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(savedRows);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(projection);
        var byKey = new Dictionary<InMemoryRelationshipKey, TEntity>();
        foreach (var row in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byKey.Add(new(key(row)), row);
        }
        var results = new List<TResult>(savedRows.Count);
        foreach (var row in savedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byKey.Remove(new(key(row)), out var saved))
                throw new InvalidOperationException("A saved batch row is missing or no longer visible after saving.");
            results.Add(projection(saved));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results.AsReadOnly();
    }
}
