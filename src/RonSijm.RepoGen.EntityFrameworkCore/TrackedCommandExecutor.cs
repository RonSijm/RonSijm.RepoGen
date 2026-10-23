using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Shared execution for explicitly mapped, single-entity commands. Never retries stale input.</summary>
public static partial class TrackedCommandExecutor
{
    public static Task<CommandResult<TResult>> CreateAsync<TEntity, TResult>(DbContext context, Func<TEntity> factory,
        Func<TEntity, TResult> result, bool save, bool reload, IRepositoryUnitOfWork? work,
        IEnumerable<string> tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, null, factory, null, null, result, delete: false, save, reload, work, tags, null, null, cancellationToken);

    public static Task<CommandResult<TResult>> PatchAsync<TEntity, TResult>(DbContext context, IQueryable<TEntity> query,
        Func<TEntity, IReadOnlyList<string>> patch, IReadOnlyDictionary<string, object?>? expectedVersions,
        Func<TEntity, TResult> result, bool save, bool reload, IRepositoryUnitOfWork? work,
        IEnumerable<string> tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, query, null, patch, expectedVersions, result, delete: false, save, reload, work, tags, null, null, cancellationToken);

    public static Task<CommandResult<TResult>> PatchWithCollectionsAsync<TEntity, TResult>(DbContext context, IQueryable<TEntity> query,
        Func<TEntity, IReadOnlyList<string>> patch, IReadOnlyDictionary<string, object?>? expectedVersions,
        Func<TEntity, TResult> result, bool save, bool reload, IRepositoryUnitOfWork? work, IEnumerable<string> tags,
        IReadOnlyList<Func<TEntity, CancellationToken, Task<TrackedCollectionChanges>>> collections,
        CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, query, null, patch, expectedVersions, result, delete: false, save, reload, work, tags, collections, null, cancellationToken);

    public static Task<CommandResult<TResult>> DeleteAsync<TEntity, TResult>(DbContext context, IQueryable<TEntity> query,
        IReadOnlyDictionary<string, object?>? expectedVersions, Func<TEntity, TResult> result, bool save,
        IRepositoryUnitOfWork? work, IEnumerable<string> tags, CancellationToken cancellationToken = default) where TEntity : class
        => ExecuteAsync(context, query, null, null, expectedVersions, result, delete: true, save, reload: false, work, tags, null, null, cancellationToken);

    private static Task<CommandResult<TResult>> ExecuteAsync<TEntity, TResult>(DbContext context, IQueryable<TEntity>? query,
        Func<TEntity>? factory, Func<TEntity, IReadOnlyList<string>>? patch, IReadOnlyDictionary<string, object?>? versions,
        Func<TEntity, TResult> result, bool delete, bool save, bool reload, IRepositoryUnitOfWork? work,
        IEnumerable<string> tags, IReadOnlyList<Func<TEntity, CancellationToken, Task<TrackedCollectionChanges>>>? collections,
        Func<TEntity, CancellationToken, Task<TResult>>? savedResult, CancellationToken cancellationToken) where TEntity : class
    {
        if (savedResult is not null && (!save || delete || reload))
            throw new ArgumentException("A saved result query requires saving a non-deleted row without reload.", nameof(savedResult));
        return collections is null ? ExecuteCoreAsync(context, query, factory, patch, versions, result, delete, save, reload, work, tags, collections, savedResult, cancellationToken)
            : ExecuteBoundedAsync();

        async Task<CommandResult<TResult>> ExecuteBoundedAsync()
        {
            ArgumentNullException.ThrowIfNull(context);
            var previous = context.ChangeTracker.LazyLoadingEnabled;
            context.ChangeTracker.LazyLoadingEnabled = false;
            try
            {
                // Relationship queries/fixup, mapper getters and saved DTOs must not trigger an
                // implicit unbounded load. Additional graph reads belong in an explicit saved-result/read query.
                return await ExecuteCoreAsync(context, query, factory, patch, versions, result, delete, save, reload, work, tags, collections, savedResult, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context.ChangeTracker.LazyLoadingEnabled = previous;
            }
        }
    }

    private static async Task<CommandResult<TResult>> ExecuteCoreAsync<TEntity, TResult>(DbContext context, IQueryable<TEntity>? query,
        Func<TEntity>? factory, Func<TEntity, IReadOnlyList<string>>? patch, IReadOnlyDictionary<string, object?>? versions,
        Func<TEntity, TResult> result, bool delete, bool save, bool reload, IRepositoryUnitOfWork? work,
        IEnumerable<string> tags, IReadOnlyList<Func<TEntity, CancellationToken, Task<TrackedCollectionChanges>>>? collections,
        Func<TEntity, CancellationToken, Task<TResult>>? savedResult, CancellationToken cancellationToken) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(tags);
        cancellationToken.ThrowIfCancellationRequested();
        var tagValues = tags.ToArray();
        foreach (var tag in tagValues)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        }

        var entity = factory is null
            ? await (query ?? throw new ArgumentNullException(nameof(query))).AsTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : factory() ?? throw new InvalidOperationException("The create mapping returned null.");
        if (entity is null)
        {
            return new(CommandStatus.NotFound);
        }

        var entry = context.Entry(entity);
        var snapshot = new TrackedEntitySnapshot(entry);
        var collectionChanges = new List<TrackedCollectionChanges>();
        void Restore()
        {
            for (var index = collectionChanges.Count - 1; index >= 0; index--)
            {
                collectionChanges[index].Restore();
            }

            snapshot.Restore();
        }

        int count;
        try
        {
            if (versions is not null)
            {
                foreach (var version in versions)
                {
                    var property = entry.Property(version.Key);
                    if (!property.Metadata.IsConcurrencyToken)
                    {
                        throw new InvalidOperationException($"'{version.Key}' is not a concurrency token.");
                    }

                    // Binary version tokens compare by contents, not request-array identity.
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
            }

            if (factory is not null)
            {
                await context.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            }
            else if (delete)
            {
                context.Remove(entity);
            }
            else
            {
                foreach (var reconcile in collections ?? [])
                {
                    collectionChanges.Add(await reconcile(entity, cancellationToken).ConfigureAwait(false));
                }

                var properties = (patch ?? throw new ArgumentNullException(nameof(patch)))(entity);
                if (properties.Count == 0 && !collectionChanges.Any(static changes => changes.HasAssignments))
                {
                    Restore();
                    return new(CommandStatus.NoChanges);
                }

                // With an expected version even an unchanged assignment must perform a conditional write.
                if (versions is { Count: > 0 })
                {
                    foreach (var name in properties)
                    {
                        entry.Property(name).IsModified = true;
                    }
                }
            }

            if (!save)
            {
                return new(CommandStatus.Staged);
            }

            count = work is null
                ? await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false)
                : await work.SaveChangesAsync(tagValues, cancellationToken).ConfigureAwait(false);
        }
        catch (TrackedCollectionConflictException)
        {
            Restore();
            return new(CommandStatus.Conflict);
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Count > 0 && exception.Entries.All(conflict =>
                   ReferenceEquals(conflict.Entity, entity) || collectionChanges.Any(changes => changes.Contains(conflict.Entity))))
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
            if (reload)
            {
                await entry.ReloadAsync(cancellationToken).ConfigureAwait(false);
            }

            var value = savedResult is null ? result(entity) : await savedResult(entity, cancellationToken).ConfigureAwait(false);
            return new(CommandStatus.Applied, count, value, hasValue: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            throw new RepositorySavedResultException(exception);
        }
    }
}
