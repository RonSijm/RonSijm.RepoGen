namespace RonSijm.RepoGen.Abstractions;

/// <summary>Validates complete reference keys against the selected owner's shared FK components.</summary>
public static class RepositoryReferenceKeys
{
    /// <remarks>Matching uses the generated typed CLR predicate, not provider collation or fixture-key comparers.
    /// All keys are checked before collection loading or mutation. Nothing is normalized or rewritten.</remarks>
    public static void RequireOwner<TOwner, TKey>(TOwner owner, IEnumerable<TKey> keys,
        Func<TOwner, TKey, bool> matchesOwner, CancellationToken cancellationToken = default)
        where TOwner : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(matchesOwner);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!matchesOwner(owner, key))
                throw new ArgumentException("Shared reference key components must match the selected owner.", nameof(keys));
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
