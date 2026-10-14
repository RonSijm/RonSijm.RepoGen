using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Projects a coordinated saved-state snapshot once after shared batch save, outside the write lock.</summary>
    public Task<CommandResult<IReadOnlyList<TResult>>> ExecuteBatchAsync<TEntity, TItem, TKey, TMatchKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, RepositoryBatchInput<TItem, TMatchKey> input,
        Func<TEntity, bool> predicate, Func<TEntity, TMatchKey> matchKey,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch, Func<TEntity, TItem, bool> expected,
        Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<IReadOnlyList<TEntity>, IReadOnlyList<TResult>> result,
        bool delete, bool save, bool forceWrite = false, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull where TMatchKey : notnull
        => ExecuteBatchCoreAsync(store, input, predicate, matchKey, patch, expected, sameValues, sameTokens, merge,
            static _ => default!, delete, save, forceWrite, result ?? throw new ArgumentNullException(nameof(result)), cancellationToken);

    /// <summary>Combines explicit field equality with one fresh result projection after shared batch save.</summary>
    public Task<CommandResult<IReadOnlyList<TResult>>> ExecuteBatchAsync<TEntity, TItem, TKey, TMatchKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, RepositoryBatchInput<TItem, TMatchKey> input,
        Func<TEntity, bool> predicate, Func<TEntity, TMatchKey> matchKey,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch, Func<TEntity, TItem, bool> expected,
        Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<IReadOnlyList<TEntity>, IReadOnlyList<TResult>> result,
        bool delete, bool save, bool forceWrite, Func<TEntity, TEntity, string, bool>? sameAssignedValue,
        CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull where TMatchKey : notnull
        => ExecuteBatchCoreAsync(store, input, predicate, matchKey, patch, expected, sameValues, sameTokens, merge,
            static _ => default!, delete, save, forceWrite, result ?? throw new ArgumentNullException(nameof(result)), cancellationToken, sameAssignedValue);
}
