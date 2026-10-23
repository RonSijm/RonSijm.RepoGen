using System.Data;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Retries an explicitly replayable transaction using the provider's execution strategy and a fresh owned context per attempt.</summary>
/// <remarks>Callbacks must not perform non-idempotent external side effects. Unknown commit outcomes still require application idempotency/verification; stale client versions are never retried into success.</remarks>
public static class RepositoryTransactionRunner
{
    public static async Task<TResult> ExecuteAsync<TContext, TResult>(IDbContextLeaseProvider<TContext> provider,
        Func<RepositoryUnitOfWork<TContext>, CancellationToken, Task<TResult>> operation,
        IEnumerable<IRepositoryCommitObserver>? observers = null, IsolationLevel isolationLevel = IsolationLevel.Serializable,
        CancellationToken cancellationToken = default) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(operation);
        await using var strategyLease = await provider.AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (!strategyLease.OwnsContext)
        {
            throw new InvalidOperationException("Execution-strategy transactions require fresh owned contexts, not borrowed/shared tracking state.");
        }

        var strategy = strategyLease.Context.Database.CreateExecutionStrategy();
        var contexts = new HashSet<DbContext>(ReferenceEqualityComparer.Instance) { strategyLease.Context };
        return await strategy.ExecuteAsync(async token =>
        {
            await using var attempt = await provider.AcquireAsync(token).ConfigureAwait(false);
            if (!attempt.OwnsContext || !contexts.Add(attempt.Context))
            {
                throw new InvalidOperationException("Each retry attempt must acquire a fresh owned context.");
            }

            if (attempt.ScopeKey != strategyLease.ScopeKey)
            {
                throw new InvalidOperationException("A retried transaction cannot switch its declared context/cache scope.");
            }

            await using var work = new RepositoryUnitOfWork<TContext>(new AttemptProvider<TContext>(attempt.Context, attempt.ScopeKey), observers);
            await using var transaction = await work.BeginTransactionAsync(isolationLevel, token).ConfigureAwait(false);
            var result = await operation(work, token).ConfigureAwait(false);
            await work.SaveChangesAsync(token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private sealed class AttemptProvider<TContext>(TContext context, string? scope) : IDbContextLeaseProvider<TContext> where TContext : DbContext
    {
        public ValueTask<DbContextLease<TContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(DbContextLease.Borrowed(context, scope));
        }
    }
}