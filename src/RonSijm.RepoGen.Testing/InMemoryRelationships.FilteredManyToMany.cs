namespace RonSijm.RepoGen.Testing;

public static partial class InMemoryRelationships
{
    /// <summary>Loads a declared edge from filtered endpoints and join rows, retaining unfiltered principal keys for fixture validation.</summary>
    /// <remarks>A filtered required target removes its link, not its owner. A filtered optional target follows
    /// the explicit missing-target policy. Truly absent required principals, duplicate principal keys and duplicate
    /// visible join pairs still fail before assignment. Hidden join rows do not participate. No query filter is
    /// evaluated here, and no related designer visibility, SQL constraints or graph persistence is inferred.</remarks>
    public static void LoadFilteredManyToMany<TSource, TTarget, TJoin>(string relationship,
        IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets, IReadOnlyList<TJoin> joins,
        IReadOnlyList<TSource> unfilteredSources, IReadOnlyList<TTarget> unfilteredTargets,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey,
        Func<TJoin, object?[]> joinSourceKey, Func<TJoin, object?[]> joinTargetKey,
        Action<TSource, List<TTarget>> assign, bool requiredSource = true, bool requiredTarget = true,
        bool preserveMissingTargets = false, IEqualityComparer<object?[]>? sourceKeyComparer = null,
        IEqualityComparer<object?[]>? targetKeyComparer = null, CancellationToken cancellationToken = default)
        where TSource : class where TTarget : class where TJoin : class
    {
        Validate(relationship, sources, targets, sourceKey, targetKey, assign, cancellationToken);
        ArgumentNullException.ThrowIfNull(unfilteredSources);
        ArgumentNullException.ThrowIfNull(unfilteredTargets);
        int? sourceSize = null, targetSize = null;
        var sourceKeys = UnfilteredKeys(relationship, unfilteredSources, sourceKey, sourceKeyComparer, ref sourceSize, cancellationToken);
        var targetKeys = UnfilteredKeys(relationship, unfilteredTargets, targetKey, targetKeyComparer, ref targetSize, cancellationToken);
        LoadManyToManyCore(relationship, sources, targets, joins, sourceKey, targetKey, joinSourceKey, joinTargetKey,
            assign, requiredSource, requiredTarget, preserveMissingTargets, sourceKeyComparer, targetKeyComparer,
            cancellationToken, new(sourceKeys, targetKeys, sourceSize, targetSize));
    }

    private static HashSet<InMemoryRelationshipKey> UnfilteredKeys<TRow>(string relationship, IReadOnlyList<TRow> rows,
        Func<TRow, object?[]> select, IEqualityComparer<object?[]>? comparer, ref int? size, CancellationToken cancellationToken)
    {
        var keys = new HashSet<InMemoryRelationshipKey>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(relationship, select(row), ref size, comparer);
            if (key is not null && !keys.Add(key)) throw Invalid(relationship, "unfiltered principal relationship keys are not unique");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return keys;
    }

    private sealed record ManyToManyAvailability(HashSet<InMemoryRelationshipKey> Sources,
        HashSet<InMemoryRelationshipKey> Targets, int? SourceSize, int? TargetSize);
}
