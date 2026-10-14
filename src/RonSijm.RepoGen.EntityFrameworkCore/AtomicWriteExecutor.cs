using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Provider capability for a unique-constraint-protected insert. Called inside an explicit operation transaction.</summary>
public interface IRepositoryAtomicWriter
{
    Task<CommandResult<TEntity>> InsertIfAbsentAsync<TEntity>(DbContext context, TEntity entity, IReadOnlyList<string> columns,
        IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class;
}

/// <summary>Coordinates insertion, optional conditional update, returned values, and commit observers in one transaction.</summary>
public static class AtomicWriteExecutor
{
    public static async Task<CommandResult<TResult>> ExecuteAsync<TEntity, TResult>(DbContext context, IRepositoryAtomicWriter writer,
        Func<TEntity> create, string[] columns, string[] conflictColumns,
        Func<TEntity, CancellationToken, Task<TEntity?>>? update, Func<TEntity, TResult> result,
        IRepositoryUnitOfWork? work, string[] tags, CancellationToken cancellationToken = default) where TEntity : class
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
            var inserted = await writer.InsertIfAbsentAsync(context, candidate, columns, conflictColumns, cancellationToken).ConfigureAwait(false);
            if (inserted.Status == CommandStatus.Inserted)
            {
                outcome = new(CommandStatus.Inserted, inserted.AffectedRows, result(inserted.Value!), hasValue: true);
            }
            else if (inserted.Status == CommandStatus.AlreadyExists && update is not null)
            {
                var updated = await update(candidate, cancellationToken).ConfigureAwait(false);
                outcome = updated is null ? new(CommandStatus.NotMatched) : new(CommandStatus.Updated, 1, result(updated), hasValue: true);
            }
            else if (inserted.Status == CommandStatus.AlreadyExists)
            {
                outcome = new(CommandStatus.AlreadyExists);
            }
            else
            {
                throw new InvalidOperationException("The atomic insert provider returned an unsupported outcome.");
            }

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