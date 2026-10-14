namespace RonSijm.RepoGen.Testing;

internal sealed class InMemoryPendingEntry<TEntity> where TEntity : class
{
    internal required TEntity Current { get; set; }
    internal TEntity? Original { get; init; }
    internal bool Deleted { get; set; }
    internal bool ForceWrite { get; set; }
    internal HashSet<string> Assigned { get; } = new(StringComparer.Ordinal);
    internal required Func<TEntity, TEntity, bool> SameValues { get; init; }
    internal required Func<TEntity, TEntity, bool> SameTokens { get; init; }
    internal required Action<TEntity, TEntity, IReadOnlyCollection<string>> Merge { get; init; }
    internal Func<TEntity, TEntity, string, bool>? SameAssignedValue { get; init; }

    internal InMemoryPendingEntry<TEntity> Clone(Func<TEntity, TEntity> copy)
    {
        var clone = new InMemoryPendingEntry<TEntity>
        {
            Current = copy(Current), Original = Original is null ? null : copy(Original), Deleted = Deleted,
            ForceWrite = ForceWrite, SameValues = SameValues, SameTokens = SameTokens, Merge = Merge, SameAssignedValue = SameAssignedValue
        };
        clone.Assigned.UnionWith(Assigned);
        return clone;
    }
}
