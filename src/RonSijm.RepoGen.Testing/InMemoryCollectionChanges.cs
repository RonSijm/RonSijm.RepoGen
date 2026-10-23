namespace RonSijm.RepoGen.Testing;

internal sealed record InMemoryCollectionChanges(bool Assigned, IReadOnlyList<object> Entries, Action Restore)
{
    internal static InMemoryCollectionChanges None { get; } = new(false, [], static () => { });
}
