using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Projects bounded updated rows from a detached unpublished root overlay and saved-only related stores.</summary>
    /// <remarks>All result/adapter/cancellation failures publish nothing. Pending writes are retained and callbacks cannot re-enter the context.</remarks>
    public Task<MutationResult<TResult>> ExecuteUpdateWithSavedResultsAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity, bool> predicate, Action<TEntity, TEntity> update, int maximumRows, string[] resultStoreNames,
        Func<IReadOnlyList<TEntity>, InMemoryDataSnapshot, IReadOnlyList<TResult>> projection, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(projection);
        if (maximumRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows), "Fresh native results require a positive explicit row bound.");
        }
        var names = OwnResultStores(resultStoreNames);
        return Run(() => Access(() => new MutationResult<TResult>(ExecuteImmediate<TEntity, TKey, TResult>(store, predicate, update, null,
            cancellationToken, projection, names, maximumRows).Items)), cancellationToken);
    }
}
