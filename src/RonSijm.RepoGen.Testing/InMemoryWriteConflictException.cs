namespace RonSijm.RepoGen.Testing;

/// <summary>A fixture row was removed or its declared token changed after staging. Not a database concurrency guarantee.</summary>
public sealed class InMemoryWriteConflictException : Exception
{
    internal InMemoryWriteConflictException(string store, object entry)
        : base("A staged in-memory write conflicted in fixture store: " + store)
    {
        Store = store;
        Entry = entry;
    }

    public string Store { get; }
    internal object Entry { get; }
}
