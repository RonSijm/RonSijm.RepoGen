using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

public static partial class TrackedBatchCommandExecutor
{
    /// <summary>Queries fresh results once after a successful shared save, receiving rows in input order.</summary>
    public static Task<CommandResult<IReadOnlyList<TResult>>> ExecuteAsync<TEntity, TItem, TKey, TResult>(
        DbContext context, IQueryable<TEntity> query, BatchCommandInput<TItem, TKey> input, Func<TEntity, TKey> key,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch,
        Func<TEntity, TItem, IReadOnlyDictionary<string, object?>>? expectedVersions,
        Func<IReadOnlyList<TEntity>, CancellationToken, Task<IReadOnlyList<TResult>>> result,
        bool delete, bool save, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
        => ExecuteCoreAsync(context, query, input, key, patch, expectedVersions, static _ => default!, delete, save, work, tags,
            result ?? throw new ArgumentNullException(nameof(result)), cancellationToken);
}
