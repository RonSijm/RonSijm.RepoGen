namespace RonSijm.RepoGen.Testing;

/// <summary>A typed, fixture-only child-store reconciliation prepared by a generated command.</summary>
/// <remarks>Does not load or persist entity navigation graphs. Created by InMemoryWriteDataContext.</remarks>
public sealed class InMemoryCollectionWrite<TParent> where TParent : class
{
    internal InMemoryCollectionWrite(InMemoryWriteDataContext context,
        Func<TParent, CancellationToken, InMemoryCollectionChanges> apply)
    {
        Context = context;
        Apply = apply;
    }

    internal InMemoryWriteDataContext Context { get; }
    internal Func<TParent, CancellationToken, InMemoryCollectionChanges> Apply { get; }
}
