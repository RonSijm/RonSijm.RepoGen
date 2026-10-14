namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Execution bookkeeping for one explicitly configured collection, not an application graph API.</summary>
public sealed class TrackedCollectionChanges
{
    public static TrackedCollectionChanges None { get; } = new(false, [], static () => { });
    private readonly Action restore;
    private readonly HashSet<object> entities;

    internal TrackedCollectionChanges(bool hasAssignments, IEnumerable<object> entities, Action restore)
    {
        HasAssignments = hasAssignments;
        this.entities = new(entities, ReferenceEqualityComparer.Instance);
        this.restore = restore;
    }

    internal bool HasAssignments { get; }
    internal bool Contains(object entity) => entities.Contains(entity);
    internal void Restore() => restore();
}
