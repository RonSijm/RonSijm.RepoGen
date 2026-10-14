namespace RonSijm.RepoGen.Testing;

public static partial class InMemoryRelationships
{
    /// <summary>Loads an explicitly declared many-to-many edge through a detached join-table snapshot.</summary>
    /// <remarks>Principal keys and join pairs must be unique. Related rows retain join-store order;
    /// callers needing deterministic result order should sort in their shared designer expression.
    /// Null key components never join. Required join references must resolve before any assignment.
    /// This does not infer inverse edges, related visibility policies, graph writes or database constraints.</remarks>
    public static void LoadManyToMany<TSource, TTarget, TJoin>(string relationship,
        IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets, IReadOnlyList<TJoin> joins,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey,
        Func<TJoin, object?[]> joinSourceKey, Func<TJoin, object?[]> joinTargetKey,
        Action<TSource, List<TTarget>> assign, bool requiredSource = true, bool requiredTarget = true,
        CancellationToken cancellationToken = default)
        where TSource : class where TTarget : class where TJoin : class
        => LoadManyToManyCore(relationship, sources, targets, joins, sourceKey, targetKey, joinSourceKey, joinTargetKey,
            assign, requiredSource, requiredTarget, preserveMissingTargets: false, sourceKeyComparer: null, targetKeyComparer: null, cancellationToken);

    /// <summary>Preserves a null slot for each missing optional target when the designed query explicitly handles nulls.</summary>
    /// <remarks>No SQL null propagation, entity identity policy, explicit loading or database FK simulation is inferred.</remarks>
    public static void LoadManyToMany<TSource, TTarget, TJoin>(string relationship,
        IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets, IReadOnlyList<TJoin> joins,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey,
        Func<TJoin, object?[]> joinSourceKey, Func<TJoin, object?[]> joinTargetKey,
        Action<TSource, List<TTarget>> assign, bool requiredSource, bool requiredTarget, bool preserveMissingTargets,
        CancellationToken cancellationToken = default)
        where TSource : class where TTarget : class where TJoin : class
        => LoadManyToManyCore(relationship, sources, targets, joins, sourceKey, targetKey, joinSourceKey, joinTargetKey,
            assign, requiredSource, requiredTarget, preserveMissingTargets, sourceKeyComparer: null, targetKeyComparer: null, cancellationToken);

    /// <summary>Loads explicitly compared principal keys and join pairs without changing null/validity policies.</summary>
    public static void LoadManyToMany<TSource, TTarget, TJoin>(string relationship,
        IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets, IReadOnlyList<TJoin> joins,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey,
        Func<TJoin, object?[]> joinSourceKey, Func<TJoin, object?[]> joinTargetKey,
        Action<TSource, List<TTarget>> assign, IEqualityComparer<object?[]>? sourceKeyComparer, IEqualityComparer<object?[]>? targetKeyComparer,
        bool requiredSource = true, bool requiredTarget = true, bool preserveMissingTargets = false, CancellationToken cancellationToken = default)
        where TSource : class where TTarget : class where TJoin : class
        => LoadManyToManyCore(relationship, sources, targets, joins, sourceKey, targetKey, joinSourceKey, joinTargetKey,
            assign, requiredSource, requiredTarget, preserveMissingTargets, sourceKeyComparer, targetKeyComparer, cancellationToken);

    private static void LoadManyToManyCore<TSource, TTarget, TJoin>(string relationship,
        IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets, IReadOnlyList<TJoin> joins,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey,
        Func<TJoin, object?[]> joinSourceKey, Func<TJoin, object?[]> joinTargetKey,
        Action<TSource, List<TTarget>> assign, bool requiredSource, bool requiredTarget, bool preserveMissingTargets,
        IEqualityComparer<object?[]>? sourceKeyComparer, IEqualityComparer<object?[]>? targetKeyComparer, CancellationToken cancellationToken)
        where TSource : class where TTarget : class where TJoin : class
    {
        if (preserveMissingTargets && requiredTarget)
            throw new ArgumentException("Missing targets can only be preserved for an optional target reference.", nameof(preserveMissingTargets));
        Validate(relationship, sources, targets, sourceKey, targetKey, assign, cancellationToken);
        ArgumentNullException.ThrowIfNull(joins);
        ArgumentNullException.ThrowIfNull(joinSourceKey);
        ArgumentNullException.ThrowIfNull(joinTargetKey);
        int? sourceSize = null, targetSize = null;
        var owners = new Dictionary<InMemoryRelationshipKey, int>();
        var related = new List<TTarget>[sources.Count];
        for (var position = 0; position < sources.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(relationship, sourceKey(sources[position]), ref sourceSize, sourceKeyComparer);
            if (key is not null && !owners.TryAdd(key, position)) throw Invalid(relationship, "owner relationship keys are not unique");
            related[position] = [];
        }
        var index = new Dictionary<InMemoryRelationshipKey, TTarget>();
        foreach (var row in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(relationship, targetKey(row), ref targetSize, targetKeyComparer);
            if (key is not null && !index.TryAdd(key, row)) throw Invalid(relationship, "target relationship keys are not unique");
        }
        var pairs = new HashSet<(InMemoryRelationshipKey Source, InMemoryRelationshipKey Target)>();
        foreach (var join in joins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var from = Key(relationship, joinSourceKey(join), ref sourceSize, sourceKeyComparer);
            cancellationToken.ThrowIfCancellationRequested();
            var to = Key(relationship, joinTargetKey(join), ref targetSize, targetKeyComparer);
            cancellationToken.ThrowIfCancellationRequested();
            var position = -1;
            TTarget? target = null;
            var hasSource = from is not null && owners.TryGetValue(from, out position);
            var hasTarget = to is not null && index.TryGetValue(to, out target);
            if (requiredSource && !hasSource) throw Invalid(relationship, "a required join reference has no matching owner in the participating store");
            if (requiredTarget && !hasTarget) throw Invalid(relationship, "a required join reference has no matching target in the participating store");
            if (from is not null && to is not null && !pairs.Add((from, to))) throw Invalid(relationship, "join relationship pairs are not unique");
            if (hasSource && (hasTarget || preserveMissingTargets)) related[position].Add(target!);
        }
        // Key/getter/validation failures never partially assign this detached edge.
        for (var position = 0; position < sources.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assign(sources[position], related[position]);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
