using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

public static partial class TrackedCommandExecutor
{
    /// <summary>Runs an asynchronous result query only after the shared save succeeds.</summary>
    public static Task<CommandResult<TResult>> CreateAsync<TEntity, TResult>(DbContext context, Func<TEntity> factory,
        Func<TEntity, CancellationToken, Task<TResult>> result, bool save, bool reload, IRepositoryUnitOfWork? work,
        IEnumerable<string> tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, null, factory, null, null, static _ => default!, delete: false, save, reload, work, tags, null,
            result ?? throw new ArgumentNullException(nameof(result)), cancellationToken);

    public static Task<CommandResult<TResult>> PatchAsync<TEntity, TResult>(DbContext context, IQueryable<TEntity> query,
        Func<TEntity, IReadOnlyList<string>> patch, IReadOnlyDictionary<string, object?>? expectedVersions,
        Func<TEntity, CancellationToken, Task<TResult>> result, bool save, bool reload, IRepositoryUnitOfWork? work,
        IEnumerable<string> tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, query, null, patch, expectedVersions, static _ => default!, delete: false, save, reload, work, tags, null,
            result ?? throw new ArgumentNullException(nameof(result)), cancellationToken);

    public static Task<CommandResult<TResult>> PatchWithCollectionsAsync<TEntity, TResult>(DbContext context, IQueryable<TEntity> query,
        Func<TEntity, IReadOnlyList<string>> patch, IReadOnlyDictionary<string, object?>? expectedVersions,
        Func<TEntity, CancellationToken, Task<TResult>> result, bool save, bool reload, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        IReadOnlyList<Func<TEntity, CancellationToken, Task<TrackedCollectionChanges>>> collections,
        CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, query, null, patch, expectedVersions, static _ => default!, delete: false, save, reload, work, tags, collections,
            result ?? throw new ArgumentNullException(nameof(result)), cancellationToken);
}
