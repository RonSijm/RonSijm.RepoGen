using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RonSijm.RepoGen.EntityFrameworkCore;

internal sealed class TrackedEntitySnapshot
{
    private readonly EntityEntry entry;
    private readonly EntityState state;
    private readonly PropertyValues current;
    private readonly PropertyValues original;
    private readonly HashSet<string> modified;

    internal TrackedEntitySnapshot(EntityEntry entry)
    {
        this.entry = entry;
        state = entry.State;
        current = entry.CurrentValues.Clone();
        original = entry.OriginalValues.Clone();
        modified = entry.Properties.Where(static property => property.IsModified).Select(static property => property.Metadata.Name).ToHashSet(StringComparer.Ordinal);
    }

    internal void Restore()
    {
        if (state == EntityState.Detached)
        {
            entry.State = EntityState.Detached;
            return;
        }

        entry.State = EntityState.Unchanged;
        entry.CurrentValues.SetValues(current);
        entry.OriginalValues.SetValues(original);
        entry.State = state;
        foreach (var property in entry.Properties)
        {
            property.IsModified = modified.Contains(property.Metadata.Name);
        }
    }
}
