namespace RonSijm.RepoGen.Testing;

internal interface IInMemoryPendingStore
{
    int Count { get; }
    InMemoryPreparedSave PrepareSave(CancellationToken cancellationToken);
    void Clear();
}
