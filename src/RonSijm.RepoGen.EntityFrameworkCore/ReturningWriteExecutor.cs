using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

public enum ReturningWriteKind
{
    Update,
    Delete
}

/// <summary>An approved mapped property and a generated source-column alias; neither is caller SQL.</summary>
public sealed record ReturningWriteAssignment(string PropertyName, string ValueAlias);

/// <summary>Infrastructure-only native DML capability. The source is an EF-translated key/value projection, never caller SQL.</summary>
public interface IRepositoryReturningWriter
{
    Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind,
        IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class;
}

/// <summary>Maps detached returned rows before commit, without saving the shared change tracker.</summary>
public static class ReturningWriteExecutor
{
    public static async Task<MutationResult<TResult>> ExecuteAsync<TEntity, TResult>(DbContext context, IRepositoryReturningWriter writer,
        IQueryable source, ReturningWriteKind kind, ReturningWriteAssignment[] assignments, Func<TEntity, TResult> projection,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(tags);
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
                var rows = await writer.ExecuteAsync<TEntity>(context, source, kind, assignments, cancellationToken).ConfigureAwait(false);
                result = new MutationResult<TResult>(rows.Select(projection));
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