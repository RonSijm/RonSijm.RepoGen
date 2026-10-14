using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Coordinates an opt-in closed-operation cache with the actual acquired context and commit boundary.</summary>
public static class RepositoryCacheCoordinator
{
    public static void Attach(IRepositoryResultCache cache, object source, string scope)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (source is IRepositoryUnitOfWork && source is not IRepositoryCommitSource)
        {
            throw new InvalidOperationException("Cached shared units of work must support commit observer registration.");
        }

        if (source is IRepositoryCommitSource commits)
        {
            commits.RegisterCommitObserver(cache, new Observer(cache, scope));
        }
    }

    public static Task<T> ReadAsync<T>(IRepositoryResultCache cache, DbContext context, string scope, string operation,
        object?[] arguments, string[] tags, RepositoryCacheOptions options, Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        cancellationToken.ThrowIfCancellationRequested();
        // Cache neither pending tracked state nor reads made inside a transaction that could still roll back.
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            return factory(cancellationToken);
        }

        var key = Partition(scope, "query:" + operation + ":" + JsonSerializer.Serialize(arguments));
        return cache.GetOrCreateAsync(key, ScopeTags(scope, tags), options, factory, cancellationToken);
    }

    public static string[] WriteTags(DbContext context, IEnumerable<string> operationTags) => operationTags.Concat(context.ChangeTracker.Entries()
        .Where(static entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
        .Select(static entry => entry.Metadata.ClrType.FullName ?? entry.Metadata.ClrType.Name)).Distinct(StringComparer.Ordinal).ToArray();

    public static async Task WrittenAsync(IRepositoryResultCache cache, object source, DbContext context, string scope, string[] tags,
        CancellationToken cancellationToken = default)
    {
        if (source is IRepositoryUnitOfWork work)
        {
            await work.RecordChangesAsync(tags, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Cache invalidation inside a transaction requires a shared commit-coordinated unit of work.");
        }

        try { await cache.InvalidateAsync(ScopeTags(scope, tags), cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) { throw new RepositoryCommitObserverException(exception); }
    }

    public static void ValidateWriteBoundary(object source, DbContext context)
    {
        if (source is not IRepositoryUnitOfWork && (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null))
        {
            throw new InvalidOperationException("Cached transactional writes require a shared commit-coordinated unit of work.");
        }
    }

    private static string[] ScopeTags(string scope, IEnumerable<string> tags) => tags.Select(tag => Partition(scope, "entity:" + tag)).Distinct(StringComparer.Ordinal).ToArray();

    private static string Partition(string scope, string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        scope.Length.ToString(CultureInfo.InvariantCulture) + ":" + scope + value)));

    private sealed class Observer(IRepositoryResultCache cache, string scope) : IRepositoryCommitObserver
    {
        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
            => cache.InvalidateAsync(ScopeTags(scope, tags), cancellationToken);
    }
}