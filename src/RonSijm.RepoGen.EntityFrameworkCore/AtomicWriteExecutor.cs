using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Coordinates insertion, optional conditional update, returned values, and commit observers in one transaction.</summary>
public static partial class AtomicWriteExecutor
{
    public static Task<CommandResult<TResult>> ExecuteAsync<TEntity, TResult>(DbContext context, IRepositoryAtomicWriter writer,
        Func<TEntity> create, string[] columns, string[] conflictColumns,
        Func<TEntity, CancellationToken, Task<TEntity?>>? update, Func<TEntity, TResult> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, create, result,
            (candidate, token) => writer.InsertIfAbsentAsync(context, candidate, columns, conflictColumns, token),
            update, work, tags, cancellationToken);

    /// <summary>Captures explicitly permitted shadow scalars under the existing atomic operation transaction.</summary>
    public static Task<CommandResult<TResult>> ExecuteWithStateAsync<TEntity, TResult>(DbContext context, IRepositoryAtomicStateWriter writer,
        Func<TEntity> create, string[] columns, string[] conflictColumns,
        Func<TEntity, CancellationToken, Task<ReturningWriteRow<TEntity>?>>? update, Func<ReturningWriteRow<TEntity>, TResult> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, create, result,
            (candidate, token) => writer.InsertIfAbsentWithStateAsync(context, candidate, columns, conflictColumns, token),
            update, work, tags, cancellationToken);

    /// <summary>Captures explicitly permitted mapped scalars, without creating EF tracker entries.</summary>
    public static Task<CommandResult<TResult>> ExecuteWithMappedStateAsync<TEntity, TResult>(DbContext context, IRepositoryAtomicMappedStateWriter writer,
        Func<TEntity> create, string[] columns, string[] conflictColumns,
        Func<TEntity, CancellationToken, Task<ReturningWriteRow<TEntity>?>>? update, Func<ReturningWriteRow<TEntity>, TResult> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, writer, create, result,
            (candidate, token) => writer.InsertIfAbsentWithMappedStateAsync(context, candidate, columns, conflictColumns, token),
            update, work, tags, cancellationToken);

    private static Task<CommandResult<TResult>> ExecuteCoreAsync<TEntity, TRow, TResult>(DbContext context, IRepositoryAtomicWriter writer,
        Func<TEntity> create, Func<TRow, TResult> result, Func<TEntity, CancellationToken, Task<CommandResult<TRow>>> insert,
        Func<TEntity, CancellationToken, Task<TRow?>>? update, IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken)
        where TEntity : class where TRow : class
    {
        ArgumentNullException.ThrowIfNull(result);
        return ExecuteCoreAsync(context, writer, create, (row, _) => Task.FromResult(result(row)), insert, update, work, tags, cancellationToken);
    }

    private static async Task<CommandResult<TResult>> ExecuteCoreAsync<TEntity, TRow, TResult>(DbContext context, IRepositoryAtomicWriter writer,
        Func<TEntity> create, Func<TRow, CancellationToken, Task<TResult>> result, Func<TEntity, CancellationToken, Task<CommandResult<TRow>>> insert,
        Func<TEntity, CancellationToken, Task<TRow?>>? update, IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken)
        where TEntity : class where TRow : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = create() ?? throw new InvalidOperationException("The atomic input mapping returned null.");
        var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        CommandResult<TResult> outcome;
        try
        {
            var inserted = await insert(candidate, cancellationToken).ConfigureAwait(false);
            if (inserted.Status == CommandStatus.Inserted)
            {
                outcome = new(CommandStatus.Inserted, inserted.AffectedRows,
                    await result(inserted.Value!, cancellationToken).ConfigureAwait(false), hasValue: true);
            }
            else if (inserted.Status == CommandStatus.AlreadyExists && update is not null)
            {
                var updated = await update(candidate, cancellationToken).ConfigureAwait(false);
                outcome = updated is null ? new(CommandStatus.NotMatched) : new(CommandStatus.Updated, 1,
                    await result(updated, cancellationToken).ConfigureAwait(false), hasValue: true);
            }
            else if (inserted.Status == CommandStatus.AlreadyExists)
            {
                outcome = new(CommandStatus.AlreadyExists);
            }
            else
            {
                throw new InvalidOperationException("The atomic insert provider returned an unsupported outcome.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }

        // Includes unsaved shared state only when the caller later saves it. This operation itself bypasses tracking.
        if (outcome.Succeeded && work is not null)
        {
            await work.RecordChangesAsync(tags, cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }
}
