namespace RonSijm.RepoGen.Testing;

/// <summary>A fixture's command view, separate from its public arrangement row shape.</summary>
/// <remarks>Created by the fixture runtime, never a provider or database abstraction.</remarks>
public abstract class InMemoryWriteStore<TEntity, TKey> where TEntity : class where TKey : notnull
{
    private protected InMemoryWriteStore() { }
    public abstract string Name { get; }
    internal abstract InMemoryDataContext Context { get; }
    internal abstract object Identity { get; }
    internal abstract IEqualityComparer<TKey> KeyComparer { get; }
    internal abstract TKey KeyOf(TEntity row);
    internal abstract TEntity Detach(TEntity row);
    internal abstract Dictionary<TKey, TEntity> CopyWriteRows();
    internal abstract Action PrepareCommitRows(Dictionary<TKey, TEntity> prepared);
    internal abstract InMemoryPreparedWrite PrepareCommitSnapshot(Dictionary<TKey, TEntity> prepared);
}
