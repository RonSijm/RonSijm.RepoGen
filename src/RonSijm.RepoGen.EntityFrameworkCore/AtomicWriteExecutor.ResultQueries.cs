using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

public static partial class AtomicWriteExecutor
{
    /// <summary>Reads the successful result inside the atomic operation transaction, before its owned commit.</summary>
    /// <remarks>Failures leave caller-owned transactions for the caller to roll back. No tracked changes are saved.</remarks>
    public static Task<CommandResult<TResult>> ExecuteWithResultQueryAsync<TEntity, TResult>(DbContext context, IRepositoryAtomicWriter writer,
        Func<TEntity> create, string[] columns, string[] conflictColumns,
        Func<TEntity, CancellationToken, Task<TEntity?>>? update, Func<TEntity, CancellationToken, Task<TResult>> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, create, result,
            (candidate, token) => writer.InsertIfAbsentAsync(context, candidate, columns, conflictColumns, token),
            update, work, tags, cancellationToken);

    /// <summary>Captures permitted shadow state, then reads a separate result query before the owned commit.</summary>
    public static Task<CommandResult<TResult>> ExecuteWithStateResultQueryAsync<TEntity, TResult>(DbContext context, IRepositoryAtomicStateWriter writer,
        Func<TEntity> create, string[] columns, string[] conflictColumns,
        Func<TEntity, CancellationToken, Task<ReturningWriteRow<TEntity>?>>? update, Func<ReturningWriteRow<TEntity>, CancellationToken, Task<TResult>> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, create, result,
            (candidate, token) => writer.InsertIfAbsentWithStateAsync(context, candidate, columns, conflictColumns, token),
            update, work, tags, cancellationToken);

    /// <summary>Captures permitted mapped state, then reads a separate result query before the owned commit.</summary>
    public static Task<CommandResult<TResult>> ExecuteWithMappedStateResultQueryAsync<TEntity, TResult>(DbContext context, IRepositoryAtomicMappedStateWriter writer,
        Func<TEntity> create, string[] columns, string[] conflictColumns,
        Func<TEntity, CancellationToken, Task<ReturningWriteRow<TEntity>?>>? update, Func<ReturningWriteRow<TEntity>, CancellationToken, Task<TResult>> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, create, result,
            (candidate, token) => writer.InsertIfAbsentWithMappedStateAsync(context, candidate, columns, conflictColumns, token),
            update, work, tags, cancellationToken);
}
