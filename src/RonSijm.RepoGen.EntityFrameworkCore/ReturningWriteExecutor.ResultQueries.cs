using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

public static partial class ReturningWriteExecutor
{
    /// <summary>Reads bounded fresh results of a native update under its owned transaction or borrowed savepoint.</summary>
    public static Task<MutationResult<TResult>> ExecuteUpdateWithResultQueryAsync<TEntity, TResult>(DbContext context, IRepositoryReturningWriter writer,
        IQueryable source, ReturningWriteAssignment[] assignments, int maximumRows,
        Func<IReadOnlyList<TEntity>, CancellationToken, Task<IReadOnlyList<TResult>>> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync<TEntity, TResult>(context, writer, source, assignments, static _ => default!,
            token => writer.ExecuteAsync<TEntity>(context, source, ReturningWriteKind.Update, assignments, token), work, tags, cancellationToken,
            result ?? throw new ArgumentNullException(nameof(result)), maximumRows);

    /// <summary>Captures permitted shadow state, then reads a separate bounded result query before commit/release.</summary>
    public static Task<MutationResult<TResult>> ExecuteUpdateWithStateResultQueryAsync<TEntity, TResult>(DbContext context, IRepositoryReturningStateWriter writer,
        IQueryable source, ReturningWriteAssignment[] assignments, int maximumRows,
        Func<IReadOnlyList<ReturningWriteRow<TEntity>>, CancellationToken, Task<IReadOnlyList<TResult>>> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync<ReturningWriteRow<TEntity>, TResult>(context, writer, source, assignments, static _ => default!,
            token => writer.ExecuteWithStateAsync<TEntity>(context, source, ReturningWriteKind.Update, assignments, token), work, tags, cancellationToken,
            result ?? throw new ArgumentNullException(nameof(result)), maximumRows);

    /// <summary>Captures permitted mapped state, then reads a separate bounded result query before commit/release.</summary>
    public static Task<MutationResult<TResult>> ExecuteUpdateWithMappedStateResultQueryAsync<TEntity, TResult>(DbContext context, IRepositoryReturningMappedStateWriter writer,
        IQueryable source, ReturningWriteAssignment[] assignments, int maximumRows,
        Func<IReadOnlyList<ReturningWriteRow<TEntity>>, CancellationToken, Task<IReadOnlyList<TResult>>> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync<ReturningWriteRow<TEntity>, TResult>(context, writer, source, assignments, static _ => default!,
            token => writer.ExecuteWithMappedStateAsync<TEntity>(context, source, ReturningWriteKind.Update, assignments, token), work, tags, cancellationToken,
            result ?? throw new ArgumentNullException(nameof(result)), maximumRows);
}
