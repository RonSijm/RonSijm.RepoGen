using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

public static partial class TrackedBatchCommandExecutor
{
    internal static void ValidateSaveBoundary(DbContext context)
    {
        if (!context.Database.IsRelational() || context.Database.AutoTransactionBehavior == AutoTransactionBehavior.Never)
            throw new InvalidOperationException("Batch saves require a relational provider with automatic transactions enabled; partial saves are not supported.");
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Batch saves require a context-local transaction, not an ambient TransactionScope.");
        if (context.Database.CurrentTransaction is { } transaction && (!transaction.SupportsSavepoints || !context.Database.AutoSavepointsEnabled))
            throw new InvalidOperationException("Batch saves in an existing transaction require enabled, supported savepoints so a failed save cannot leave partial database changes.");
    }
}
