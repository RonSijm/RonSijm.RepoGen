using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Maps native values or explicit fresh result queries before commit, without saving the shared change tracker.</summary>
public static partial class ReturningWriteExecutor
{
    public static Task<MutationResult<TResult>> ExecuteAsync<TEntity, TResult>(DbContext context, IRepositoryReturningWriter writer,
        IQueryable source, ReturningWriteKind kind, ReturningWriteAssignment[] assignments, Func<TEntity, TResult> projection,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, source, assignments, projection,
            token => writer.ExecuteAsync<TEntity>(context, source, kind, assignments, token), work, tags, cancellationToken);

    /// <summary>Maps explicitly opted-in detached property values under the same commit/savepoint boundary.</summary>
    public static Task<MutationResult<TResult>> ExecuteWithStateAsync<TEntity, TResult>(DbContext context, IRepositoryReturningStateWriter writer,
        IQueryable source, ReturningWriteKind kind, ReturningWriteAssignment[] assignments, Func<ReturningWriteRow<TEntity>, TResult> projection,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, source, assignments, projection,
            token => writer.ExecuteWithStateAsync<TEntity>(context, source, kind, assignments, token), work, tags, cancellationToken);

    /// <summary>Maps explicitly captured mapped scalars under the ordinary native transaction/savepoint boundary.</summary>
    public static Task<MutationResult<TResult>> ExecuteWithMappedStateAsync<TEntity, TResult>(DbContext context, IRepositoryReturningMappedStateWriter writer,
        IQueryable source, ReturningWriteKind kind, ReturningWriteAssignment[] assignments, Func<ReturningWriteRow<TEntity>, TResult> projection,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, source, assignments, projection,
            token => writer.ExecuteWithMappedStateAsync<TEntity>(context, source, kind, assignments, token), work, tags, cancellationToken);

    private static async Task<MutationResult<TResult>> ExecuteCoreAsync<TRow, TResult>(DbContext context, IRepositoryReturningWriter writer,
        IQueryable source, ReturningWriteAssignment[] assignments, Func<TRow, TResult> projection,
        Func<CancellationToken, Task<IReadOnlyList<TRow>>> readRows, IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken,
        Func<IReadOnlyList<TRow>, CancellationToken, Task<IReadOnlyList<TResult>>>? resultQuery = null, int? maximumRows = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(tags);
        if (resultQuery is not null && maximumRows is not > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows), "Fresh native results require a positive explicit row bound.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (System.Transactions.Transaction.Current is not null)
        {
            throw new NotSupportedException("Returning writes require an explicit context transaction, not an ambient transaction.");
        }

        var existing = context.Database.CurrentTransaction;
        if (existing is not null && !existing.SupportsSavepoints)
        {
            throw new NotSupportedException("Returning writes in an existing transaction require savepoint support so mapping failures can be rolled back.");
        }

        var owned = existing is null ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        var savepoint = existing is null ? null : "RGReturn" + Guid.NewGuid().ToString("N")[..24];
        MutationResult<TResult> result;
        try
        {
            if (savepoint is not null)
            {
                await existing!.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var rows = await readRows(cancellationToken).ConfigureAwait(false);
                if (resultQuery is null)
                {
                    result = new MutationResult<TResult>(rows.Select(projection));
                }
                else
                {
                    if (rows.Count > maximumRows)
                    {
                        throw new InvalidOperationException("The native update exceeded the configured fresh-result row bound.");
                    }
                    var items = rows.Count == 0 ? Array.Empty<TResult>() : await resultQuery(rows, cancellationToken).ConfigureAwait(false);
                    result = new MutationResult<TResult>(items);
                    if (result.AffectedRows != rows.Count)
                    {
                        throw new InvalidOperationException("A fresh native result must contain exactly one value per directly updated row.");
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                // In a shared transaction, defer observers to its commit. Mapping failures have not recorded changes.
                if (existing is not null && result.AffectedRows > 0 && work is not null)
                {
                    await work.RecordChangesAsync(tags, cancellationToken).ConfigureAwait(false);
                }

                if (savepoint is not null)
                {
                    await existing!.ReleaseSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
                }

                if (owned is not null)
                {
                    await owned.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception failure)
            {
                // Do not use an already-cancelled token to restore the caller's transaction.
                if (savepoint is not null)
                {
                    try
                    {
                        await existing!.RollbackToSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
                        await existing.ReleaseSavepointAsync(savepoint, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollbackFailure) { throw new AggregateException("Returning write failed and its savepoint could not be rolled back.", failure, rollbackFailure); }
                }

                throw;
            }
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (owned is not null && result.AffectedRows > 0 && work is not null)
        {
            await work.RecordChangesAsync(tags, CancellationToken.None).ConfigureAwait(false);
        }

        return result;
    }
}
