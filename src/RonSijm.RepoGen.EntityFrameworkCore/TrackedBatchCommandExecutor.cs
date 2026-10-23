using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Bounded tracked root-row batches, with one shared save and no automatic stale-input retry.</summary>
public static partial class TrackedBatchCommandExecutor
{
    public static BatchCommandInput<TItem, TKey> PrepareInput<TItem, TKey>(IEnumerable<TItem> items, Func<TItem, TKey> key,
        int maximumItems, CancellationToken cancellationToken = default) where TKey : notnull
        => new(RepositoryBatchInputs.Prepare(items, key, maximumItems, cancellationToken));

    public static Task<CommandResult<IReadOnlyList<TResult>>> ExecuteAsync<TEntity, TItem, TKey, TResult>(
        DbContext context, IQueryable<TEntity> query, BatchCommandInput<TItem, TKey> input, Func<TEntity, TKey> key,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch,
        Func<TEntity, TItem, IReadOnlyDictionary<string, object?>>? expectedVersions, Func<TEntity, TResult> result,
        bool delete, bool save, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        CancellationToken cancellationToken = default) where TEntity : class where TKey : notnull
        => ExecuteCoreAsync(context, query, input, key, patch, expectedVersions, result, delete, save, work, tags, null, cancellationToken);

    private static async Task<CommandResult<IReadOnlyList<TResult>>> ExecuteCoreAsync<TEntity, TItem, TKey, TResult>(
        DbContext context, IQueryable<TEntity> query, BatchCommandInput<TItem, TKey> input, Func<TEntity, TKey> key,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch,
        Func<TEntity, TItem, IReadOnlyDictionary<string, object?>>? expectedVersions, Func<TEntity, TResult> result,
        bool delete, bool save, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        Func<IReadOnlyList<TEntity>, CancellationToken, Task<IReadOnlyList<TResult>>>? savedResult,
        CancellationToken cancellationToken) where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(tags);
        if (savedResult is not null && (!save || delete))
            throw new ArgumentException("A saved batch result requires a saving patch, not staging or deletion.", nameof(savedResult));
        cancellationToken.ThrowIfCancellationRequested();
        var tagValues = tags.ToArray();
        foreach (var tag in tagValues)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        }

        if (input.Items.Count == 0)
        {
            return new(CommandStatus.NoChanges);
        }
        if (save)
        {
            ValidateSaveBoundary(context);
        }

        var lazyLoading = context.ChangeTracker.LazyLoadingEnabled;
        context.ChangeTracker.LazyLoadingEnabled = false;
        try
        {
            // The generator already restricts this query to the bounded keys and designer visibility.
            var rows = await query.AsTracking().Take(input.Items.Count + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            var existing = rows.ToDictionary(key);
            if (existing.Count != input.Items.Count || input.Keys.Any(value => !existing.ContainsKey(value)))
            {
                return new(CommandStatus.NotFound);
            }

            context.ChangeTracker.DetectChanges();
            if (rows.Any(row => context.Entry(row).State != EntityState.Unchanged))
            {
                throw new InvalidOperationException("Batch target rows have pending changes. Save or discard those changes before executing the batch.");
            }

            var snapshots = rows.Select(row => new TrackedEntitySnapshot(context.Entry(row))).ToArray();
            void Restore()
            {
                foreach (var snapshot in snapshots)
                {
                    snapshot.Restore();
                }
            }

            int count;
            try
            {
                var assigned = false;
                foreach (var pair in input.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = existing[pair.Key];
                    var entry = context.Entry(row);
                    var versions = expectedVersions?.Invoke(row, pair.Value);
                    foreach (var version in versions ?? new Dictionary<string, object?>())
                    {
                        var property = entry.Property(version.Key);
                        if (!property.Metadata.IsConcurrencyToken)
                        {
                            throw new InvalidOperationException($"'{version.Key}' is not a concurrency token.");
                        }

                        var equal = property.OriginalValue is byte[] actual && version.Value is byte[] expected
                            ? actual.AsSpan().SequenceEqual(expected)
                            : property.Metadata.GetValueComparer()?.Equals(property.OriginalValue, version.Value) ?? Equals(property.OriginalValue, version.Value);
                        if (!equal)
                        {
                            Restore();
                            return new(CommandStatus.Conflict);
                        }

                        property.OriginalValue = version.Value;
                    }

                    if (delete)
                    {
                        context.Remove(row);
                        assigned = true;
                    }
                    else
                    {
                        var properties = (patch ?? throw new ArgumentNullException(nameof(patch)))(row, pair.Value);
                        assigned |= properties.Count > 0;
                        foreach (var name in properties)
                        {
                            var property = entry.Property(name);
                            var changed = !(property.Metadata.GetValueComparer()?.Equals(property.OriginalValue, property.CurrentValue) ?? Equals(property.OriginalValue, property.CurrentValue));
                            // Explicit mapped changes must persist even when automatic detection is disabled.
                            if (versions is { Count: > 0 } || changed)
                            {
                                property.IsModified = true;
                            }
                        }
                    }
                }

                if (!assigned)
                {
                    Restore();
                    return new(CommandStatus.NoChanges);
                }

                if (!save)
                {
                    return new(CommandStatus.Staged);
                }
                count = work is null ? await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false)
                    : await work.SaveChangesAsync(tagValues, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException exception) when (exception.Entries.Count > 0 &&
                exception.Entries.All(conflict => rows.Any(row => ReferenceEquals(row, conflict.Entity))))
            {
                Restore();
                return new(CommandStatus.Conflict);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                Restore();
                throw new RepositoryWriteConflictException(exception);
            }
            catch (Exception exception) when (exception is not RepositoryCommitException)
            {
                Restore();
                throw;
            }

            try
            {
                if (savedResult is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var savedRows = input.Items.Select(pair => existing[pair.Key]).ToArray();
                    var savedValues = await savedResult(savedRows, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (savedValues is null || savedValues.Count != savedRows.Length)
                        throw new InvalidOperationException("A saved batch result must return exactly one value per saved input row.");
                    return new(CommandStatus.Applied, count, savedValues, hasValue: true);
                }
                var values = new List<TResult>(input.Items.Count);
                foreach (var pair in input.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    values.Add(result(existing[pair.Key]));
                }

                return new(CommandStatus.Applied, count, values.AsReadOnly(), hasValue: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                throw new RepositorySavedResultException(exception);
            }
        }
        finally
        {
            context.ChangeTracker.LazyLoadingEnabled = lazyLoading;
        }
    }
}
