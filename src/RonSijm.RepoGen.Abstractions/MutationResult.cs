namespace RonSijm.RepoGen.Abstractions;

/// <summary>Detached application values returned by one immediate, set-based mutation.</summary>
/// <remarks>Items have no defined order. Zero items means no rows matched, not proof of absence versus a version conflict.
/// An enclosing transaction still controls persistence. Cascades and trigger side effects are not included.</remarks>
public sealed class MutationResult<T>
{
    public MutationResult(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = Array.AsReadOnly(items.ToArray());
    }

    public IReadOnlyList<T> Items { get; }
    public int AffectedRows => Items.Count;
}