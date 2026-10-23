using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Projects a successful insert from detached prepared root rows and saved-only related stores, before publication.</summary>
    /// <remarks>Result/copy failures publish nothing. Callbacks must not re-enter the context. Pending writes are not flushed.</remarks>
    public Task<CommandResult<TResult>> ExecuteInsertIfAbsentWithSavedResultAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity> create, Func<TEntity, TEntity, bool> sameConflict, string[] resultStoreNames,
        Func<TEntity, InMemoryDataSnapshot, TResult> projection, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(projection);
        var names = OwnResultStores(resultStoreNames);
        return Run(() => Access(() => ExecuteInsert(store, create, sameConflict, null, null, _ => default!, null,
            cancellationToken, projection, names)), cancellationToken);
    }

    /// <summary>Projects a successful insert/update from an unpublished root overlay and saved-only related stores.</summary>
    /// <remarks>This deterministic fixture simulation does not promise relational locks, uniqueness races or provider rollback behavior.</remarks>
    public Task<CommandResult<TResult>> ExecuteInsertOrUpdateWithSavedResultAsync<TEntity, TKey, TResult>(InMemoryWriteStore<TEntity, TKey> store,
        Func<TEntity> create, Func<TEntity, TEntity, bool> sameConflict, Func<TEntity, bool> predicate,
        Action<TEntity, TEntity> update, string[] resultStoreNames, Func<TEntity, InMemoryDataSnapshot, TResult> projection,
        Func<TEntity, bool>? updatedResultFilter = null, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(projection);
        var names = OwnResultStores(resultStoreNames);
        return Run(() => Access(() => ExecuteInsert(store, create, sameConflict, predicate, update, _ => default!, updatedResultFilter,
            cancellationToken, projection, names)), cancellationToken);
    }

    private static string[] OwnResultStores(string[] resultStoreNames)
    {
        ArgumentNullException.ThrowIfNull(resultStoreNames);
        var names = resultStoreNames.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        }
        return names;
    }
}
