using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Read-only counted-page scope. Existing transactions must offer a stable snapshot or serializable isolation.</summary>
public sealed class RepositoryReadTransaction : IAsyncDisposable
{
    private readonly IDbContextTransaction? owned;
    private RepositoryReadTransaction(IDbContextTransaction? owned) => this.owned = owned;

    public static async ValueTask<RepositoryReadTransaction> AcquireAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Consistent counted pages require an explicit context transaction, not an ambient transaction.");
        }

        if (context.Database.CurrentTransaction is { } transaction)
        {
            if (transaction.GetDbTransaction().IsolationLevel is not IsolationLevel.Serializable and not IsolationLevel.Snapshot)
            {
                throw new InvalidOperationException("A consistent counted page requires Serializable or Snapshot isolation in the existing transaction.");
            }

            return new(null);
        }

        return new(await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false));
    }

    // This read scope does not save or commit pending tracked writes.
    public ValueTask DisposeAsync() => owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}