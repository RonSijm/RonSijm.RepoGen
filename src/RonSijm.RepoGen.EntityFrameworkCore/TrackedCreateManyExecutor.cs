using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>All-target preparation, bounded scalar creation and one shared tracked save.</summary>
public static class TrackedCreateManyExecutor
{
    public static Task<CommandResult<IReadOnlyList<TResult>>> ExecuteAsync<TEntity, TItem, TResult>(
        DbContext context, IReadOnlyList<TItem> input, Func<TItem, TEntity> factory, Func<TEntity, TResult> result,
        bool save, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, input, factory, result, save, work, tags, null, cancellationToken);

    public static Task<CommandResult<IReadOnlyList<TResult>>> ExecuteAsync<TEntity, TItem, TResult>(
        DbContext context, IReadOnlyList<TItem> input, Func<TItem, TEntity> factory,
        Func<IReadOnlyList<TEntity>, CancellationToken, Task<IReadOnlyList<TResult>>> savedResult,
        bool save, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteCoreAsync(context, input, factory, static _ => default!, save, work, tags,
            savedResult ?? throw new ArgumentNullException(nameof(savedResult)), cancellationToken);

    private static async Task<CommandResult<IReadOnlyList<TResult>>> ExecuteCoreAsync<TEntity, TItem, TResult>(
        DbContext context, IReadOnlyList<TItem> input, Func<TItem, TEntity> factory, Func<TEntity, TResult> result,
        bool save, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        Func<IReadOnlyList<TEntity>, CancellationToken, Task<IReadOnlyList<TResult>>>? savedResult,
        CancellationToken cancellationToken) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(tags);
        if (savedResult is not null && !save) throw new ArgumentException("A saved create-batch result requires Save.", nameof(savedResult));
        cancellationToken.ThrowIfCancellationRequested();
        var tagValues = tags.ToArray();
        foreach (var tag in tagValues) ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (input.Count == 0) return new(CommandStatus.NoChanges);
        if (save) TrackedBatchCommandExecutor.ValidateSaveBoundary(context);
        var lazyLoading = context.ChangeTracker.LazyLoadingEnabled;
        context.ChangeTracker.LazyLoadingEnabled = false;
        try
        {
            var rows = new List<TEntity>(input.Count);
            var identities = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);
            foreach (var item in input)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is null) throw new ArgumentException("Create batch items must be non-null.", nameof(input));
                var row = factory(item) ?? throw new InvalidOperationException("The create mapping returned null.");
                cancellationToken.ThrowIfCancellationRequested();
                if (!identities.Add(row) || context.Entry(row).State != EntityState.Detached)
                    throw new InvalidOperationException("Each create-batch mapping must return a distinct untracked entity.");
                foreach (var navigation in context.Entry(row).Navigations)
                {
                    var value = navigation.CurrentValue;
                    if (value is not null && (!navigation.Metadata.IsCollection || ((System.Collections.IEnumerable)value).Cast<object>().Any()))
                        throw new InvalidOperationException("Scalar create batches cannot contain populated navigation graphs.");
                }
                rows.Add(row);
            }
            var snapshots = rows.Select(row => new TrackedEntitySnapshot(context.Entry(row))).ToArray();
            var earlierEntries = context.ChangeTracker.Entries().ToArray();
            void Restore()
            {
                // A failed save discards store-generated values. Detect the reverted keys while the
                // entries are still tracked so EF updates its identity maps before they are detached.
                context.ChangeTracker.DetectChanges();
                foreach (var snapshot in snapshots) snapshot.Restore();
                // Detaching does not remove every CLR relationship-fixup reference. Otherwise a later
                // DetectChanges can reattach failed insertions through an earlier tracked principal.
                foreach (var entry in earlierEntries)
                foreach (var navigation in entry.Navigations)
                {
                    if (navigation.Metadata.IsCollection && navigation.CurrentValue is System.Collections.IEnumerable collection)
                    {
                        foreach (var row in collection.Cast<object>().OfType<TEntity>().Where(identities.Contains).ToArray())
                            navigation.Metadata.GetCollectionAccessor()!.Remove(entry.Entity, row);
                    }
                    else if (navigation.CurrentValue is TEntity row && identities.Contains(row)) navigation.CurrentValue = null;
                }
            }
            int count;
            try
            {
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await context.AddAsync(row, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!save) return new(CommandStatus.Staged);
                count = work is null ? await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false)
                    : await work.SaveChangesAsync(tagValues, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException exception) when (exception.Entries.Count > 0 && exception.Entries.All(entry => entry.Entity is TEntity row && identities.Contains(row)))
            { Restore(); return new(CommandStatus.Conflict); }
            catch (DbUpdateConcurrencyException exception) { Restore(); throw new RepositoryWriteConflictException(exception); }
            catch (Exception exception) when (exception is not RepositoryCommitException) { Restore(); throw; }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<TResult> values;
                if (savedResult is not null)
                {
                    values = await savedResult(rows.AsReadOnly(), cancellationToken).ConfigureAwait(false);
                    if (values is null || values.Count != rows.Count)
                        throw new InvalidOperationException("A saved create-batch result must return exactly one value per input row.");
                }
                else
                {
                    var projected = new List<TResult>(rows.Count);
                    foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); projected.Add(result(row)); }
                    values = projected.AsReadOnly();
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new(CommandStatus.Applied, count, values, hasValue: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            { throw new RepositorySavedResultException(exception); }
        }
        finally { context.ChangeTracker.LazyLoadingEnabled = lazyLoading; }
    }
}
