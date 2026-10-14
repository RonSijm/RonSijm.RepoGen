namespace RonSijm.RepoGen.Testing;

/// <summary>Loads one explicitly declared relationship on detached read snapshots. Never changes fixture stores.</summary>
/// <remarks>Null key components never join. Keys use ordinal CLR/structural equality, not SQL collation or EF
/// value comparers unless a fixture component policy is explicitly supplied. Reference targets and collection owners must have unique relationship keys. Required
/// references must resolve; invalid fixture rows fail instead of inventing a related entity. Callbacks are
/// generated property assignments, not persistence hooks, and run outside the fixture context lock.</remarks>
public static partial class InMemoryRelationships
{
    public static void LoadReference<TSource, TTarget>(string relationship, IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey, Action<TSource, TTarget?> assign,
        bool required = false, CancellationToken cancellationToken = default) where TSource : class where TTarget : class
        => LoadReference(relationship, sources, targets, sourceKey, targetKey, assign, keyComparer: null, required, cancellationToken);

    public static void LoadReference<TSource, TTarget>(string relationship, IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey, Action<TSource, TTarget?> assign,
        IEqualityComparer<object?[]>? keyComparer, bool required = false, CancellationToken cancellationToken = default) where TSource : class where TTarget : class
    {
        Validate(relationship, sources, targets, sourceKey, targetKey, assign, cancellationToken);
        int? size = null;
        var index = new Dictionary<InMemoryRelationshipKey, TTarget>();
        foreach (var row in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(relationship, targetKey(row), ref size, keyComparer);
            if (key is not null && !index.TryAdd(key, row)) throw Invalid(relationship, "target relationship keys are not unique");
        }
        var related = new TTarget?[sources.Count];
        for (var position = 0; position < sources.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(relationship, sourceKey(sources[position]), ref size, keyComparer);
            if (key is not null) index.TryGetValue(key, out related[position]);
            if (required && related[position] is null) throw Invalid(relationship, "a required reference has no matching target in the participating store");
        }
        // Key/getter/validation failures never partially assign an edge.
        for (var position = 0; position < sources.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assign(sources[position], related[position]);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static void LoadCollection<TSource, TTarget>(string relationship, IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey, Action<TSource, List<TTarget>> assign,
        CancellationToken cancellationToken = default) where TSource : class where TTarget : class
        => LoadCollection(relationship, sources, targets, sourceKey, targetKey, assign, keyComparer: null, cancellationToken);

    public static void LoadCollection<TSource, TTarget>(string relationship, IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets,
        Func<TSource, object?[]> sourceKey, Func<TTarget, object?[]> targetKey, Action<TSource, List<TTarget>> assign,
        IEqualityComparer<object?[]>? keyComparer, CancellationToken cancellationToken = default) where TSource : class where TTarget : class
    {
        Validate(relationship, sources, targets, sourceKey, targetKey, assign, cancellationToken);
        int? size = null;
        var index = new Dictionary<InMemoryRelationshipKey, List<TTarget>>();
        foreach (var row in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(relationship, targetKey(row), ref size, keyComparer);
            if (key is null) continue;
            if (!index.TryGetValue(key, out var rows)) index.Add(key, rows = []);
            rows.Add(row);
        }
        var seen = new HashSet<InMemoryRelationshipKey>();
        var related = new List<TTarget>[sources.Count];
        for (var position = 0; position < sources.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(relationship, sourceKey(sources[position]), ref size, keyComparer);
            if (key is not null && !seen.Add(key)) throw Invalid(relationship, "owner relationship keys are not unique");
            related[position] = key is not null && index.TryGetValue(key, out var rows) ? new(rows) : [];
        }
        for (var position = 0; position < sources.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            assign(sources[position], related[position]);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void Validate(string relationship, object sources, object targets, Delegate sourceKey, Delegate targetKey,
        Delegate assign, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationship);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(targetKey);
        ArgumentNullException.ThrowIfNull(assign);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static InMemoryRelationshipKey? Key(string relationship, object?[]? parts, ref int? size, IEqualityComparer<object?[]>? comparer = null)
    {
        if (parts is null || parts.Length == 0) throw Invalid(relationship, "key selectors must return at least one component");
        size ??= parts.Length;
        if (size != parts.Length) throw Invalid(relationship, "key selectors returned different component counts");
        if (comparer is InMemoryRelationshipKeyComparer declared) declared.Validate(parts);
        return parts.Any(static part => part is null) ? null : new(parts, comparer);
    }

    private static InvalidOperationException Invalid(string relationship, string reason) =>
        new("In-memory relationship '" + relationship + "': " + reason + ".");
}
