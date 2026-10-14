using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Owner-scoped, bounded leaf-collection execution used by generated tracked commands.</summary>
public static class TrackedCollectionReconciler
{
    /// <summary>Requires a save boundary that can roll back all join-row changes if the shared save fails.</summary>
    public static void ValidateSaveBoundary(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TrackedBatchCommandExecutor.ValidateSaveBoundary(context);
    }

    public static CollectionReconciliationInput<TItem, TKey> PrepareInput<TItem, TKey>(IEnumerable<TItem> items,
        Func<TItem, TKey> key, int maximumItems, CancellationToken cancellationToken = default) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumItems, int.MaxValue - 1);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = new Dictionary<TKey, TItem>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Count == maximumItems)
            {
                throw new ArgumentException($"Collection input exceeds its limit of {maximumItems} items.", nameof(items));
            }

            if (item is null)
            {
                throw new ArgumentException("Collection input cannot contain null items.", nameof(items));
            }

            var value = key(item);
            cancellationToken.ThrowIfCancellationRequested();
            if (value is null || !snapshot.TryAdd(value, item))
            {
                throw new ArgumentException("Collection input keys must be non-null and unique.", nameof(items));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(snapshot, maximumItems);
    }

    public static Task<TrackedCollectionChanges> ApplyAsync<TEntity, TChild, TItem, TKey>(
        DbContext context, TEntity entity, string navigationName, Func<TEntity, ICollection<TChild>> navigation,
        CollectionReconciliationInput<TItem, TKey>? input, Func<TChild, TKey> childKey,
        Func<TItem, TChild>? create, Func<TChild, TItem, IReadOnlyList<string>>? update,
        Func<TChild, TItem, IReadOnlyDictionary<string, object?>>? expectedVersions,
        bool deleteMissing, bool rejectMissing, Func<IQueryable<TChild>, IQueryable<TChild>> filter,
        CancellationToken cancellationToken = default)
        where TEntity : class where TChild : class where TKey : notnull
        => ApplyCoreAsync(context, entity, navigationName, navigation, input, childKey, create, update, expectedVersions,
            deleteMissing, rejectMissing, filter, linkRowsOnly: false, cancellationToken);

    /// <summary>Executes designed link-row reconciliation without allowing mapped endpoint graphs.</summary>
    public static Task<TrackedCollectionChanges> ApplyLinksAsync<TEntity, TChild, TItem, TKey>(
        DbContext context, TEntity entity, string navigationName, Func<TEntity, ICollection<TChild>> navigation,
        CollectionReconciliationInput<TItem, TKey>? input, Func<TChild, TKey> childKey,
        Func<TItem, TChild>? create, Func<TChild, TItem, IReadOnlyList<string>>? update,
        Func<TChild, TItem, IReadOnlyDictionary<string, object?>>? expectedVersions,
        bool deleteMissing, bool rejectMissing, Func<IQueryable<TChild>, IQueryable<TChild>> filter,
        CancellationToken cancellationToken = default)
        where TEntity : class where TChild : class where TKey : notnull
        => ApplyCoreAsync(context, entity, navigationName, navigation, input, childKey, create, update, expectedVersions,
            deleteMissing, rejectMissing, filter, linkRowsOnly: true, cancellationToken);

    /// <summary>Checks shared owner/reference key components before loading or mutating any link rows.</summary>
    public static Task<TrackedCollectionChanges> ApplyLinksAsync<TEntity, TChild, TItem, TKey>(
        DbContext context, TEntity entity, string navigationName, Func<TEntity, ICollection<TChild>> navigation,
        CollectionReconciliationInput<TItem, TKey>? input, Func<TChild, TKey> childKey,
        Func<TItem, TChild>? create, Func<TChild, TItem, IReadOnlyList<string>>? update,
        Func<TChild, TItem, IReadOnlyDictionary<string, object?>>? expectedVersions,
        bool deleteMissing, bool rejectMissing, Func<IQueryable<TChild>, IQueryable<TChild>> filter,
        Func<TEntity, TKey, bool> matchesOwner, CancellationToken cancellationToken = default)
        where TEntity : class where TChild : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(matchesOwner);
        if (input is not null)
            RonSijm.RepoGen.Abstractions.RepositoryReferenceKeys.RequireOwner(entity, input.Items.Keys, matchesOwner, cancellationToken);
        return ApplyCoreAsync(context, entity, navigationName, navigation, input, childKey, create, update, expectedVersions,
            deleteMissing, rejectMissing, filter, linkRowsOnly: true, cancellationToken);
    }

    private static async Task<TrackedCollectionChanges> ApplyCoreAsync<TEntity, TChild, TItem, TKey>(
        DbContext context, TEntity entity, string navigationName, Func<TEntity, ICollection<TChild>> navigation,
        CollectionReconciliationInput<TItem, TKey>? input, Func<TChild, TKey> childKey,
        Func<TItem, TChild>? create, Func<TChild, TItem, IReadOnlyList<string>>? update,
        Func<TChild, TItem, IReadOnlyDictionary<string, object?>>? expectedVersions,
        bool deleteMissing, bool rejectMissing, Func<IQueryable<TChild>, IQueryable<TChild>> filter,
        bool linkRowsOnly, CancellationToken cancellationToken)
        where TEntity : class where TChild : class where TKey : notnull
    {
        if (input is null)
        {
            return TrackedCollectionChanges.None;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entry = context.Entry(entity);
        var relationship = entry.Collection<TChild>(navigationName);
        // Query() scopes by the selected principal's FK and retains EF global filters. Do not use
        // the navigation's existing members as the mutation scope (fixup can contain hidden rows).
        var query = filter(relationship.Query().AsTracking());
        var rows = await query.Take(input.MaximumItems + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count > input.MaximumItems)
        {
            throw new InvalidOperationException($"Existing collection '{navigationName}' exceeds its limit of {input.MaximumItems} items.");
        }

        var collection = navigation(entity) ?? throw new InvalidOperationException($"Collection '{navigationName}' must be initialized.");
        if (collection.IsReadOnly)
        {
            throw new InvalidOperationException($"Collection '{navigationName}' must be writable to reconcile its membership safely.");
        }

        context.ChangeTracker.DetectChanges();
        if (collection.Concat(rows).Any(child => context.Entry(child).State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException($"Collection '{navigationName}' has pending changes. Save or discard them before reconciling it.");
        }

        var existing = rows.ToDictionary(childKey);
        var missing = rows.Where(child => !input.Items.ContainsKey(childKey(child))).ToArray();
        if (rejectMissing && missing.Length != 0)
        {
            throw new ArgumentException($"Collection '{navigationName}' does not permit missing existing keys.");
        }

        var additions = new List<TChild>();
        foreach (var item in input.Items.Where(item => !existing.ContainsKey(item.Key)))
        {
            if (create is null)
            {
                throw new ArgumentException($"Collection '{navigationName}' does not permit new or unknown keys.");
            }

            var child = create(item.Value) ?? throw new InvalidOperationException("The child create mapping returned null.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!EqualityComparer<TKey>.Default.Equals(childKey(child), item.Key) || context.Entry(child).State != EntityState.Detached)
            {
                throw new InvalidOperationException("A child create mapping must return a new detached entity with the supplied key.");
            }

            if (linkRowsOnly)
                foreach (var edge in context.Entry(child).Navigations)
                {
                    var value = edge.CurrentValue;
                    if (value is not null && (!edge.Metadata.IsCollection || ((System.Collections.IEnumerable)value).Cast<object>().Any()))
                        throw new InvalidOperationException("Link mappings must create scalar join rows only, not populated endpoint/navigation graphs.");
                }

            additions.Add(child);
        }

        // Hidden fixup members are untouched: snapshot only the bounded, queried mutation scope.
        var members = rows.Where(collection.Contains).ToArray();
        var snapshots = rows.Select(child => new TrackedEntitySnapshot(context.Entry(child))).ToArray();
        var assigned = additions.Count != 0 || deleteMissing && missing.Length != 0;
        void Restore()
        {
            // Failed saves discard generated keys. Repair their identity-map entries while link
            // additions are still tracked, including when automatic detection is disabled.
            if (linkRowsOnly) context.ChangeTracker.DetectChanges();
            foreach (var child in additions)
            {
                context.Entry(child).State = EntityState.Detached;
                collection.Remove(child);
            }

            foreach (var child in members)
            {
                if (!collection.Contains(child))
                {
                    collection.Add(child);
                }
            }

            foreach (var snapshot in snapshots)
            {
                snapshot.Restore();
            }
        }

        try
        {
            // Check all supplied child versions before applying any setters.
            foreach (var item in input.Items.Where(item => existing.ContainsKey(item.Key)))
            {
                if (expectedVersions is null)
                {
                    continue;
                }

                var child = context.Entry(existing[item.Key]);
                foreach (var version in expectedVersions(existing[item.Key], item.Value))
                {
                    var property = child.Property(version.Key);
                    if (!property.Metadata.IsConcurrencyToken)
                    {
                        throw new InvalidOperationException($"'{version.Key}' is not a concurrency token.");
                    }

                    var equal = property.OriginalValue is byte[] actual && version.Value is byte[] expected
                        ? actual.AsSpan().SequenceEqual(expected)
                        : property.Metadata.GetValueComparer()?.Equals(property.OriginalValue, version.Value) ?? Equals(property.OriginalValue, version.Value);
                    if (!equal)
                    {
                        throw new TrackedCollectionConflictException();
                    }

                    property.OriginalValue = version.Value;
                }
            }

            foreach (var item in input.Items.Where(item => existing.ContainsKey(item.Key)))
            {
                if (update is null)
                {
                    continue;
                }

                var child = existing[item.Key];
                var properties = update(child, item.Value);
                assigned |= properties.Count != 0;
                if (expectedVersions is not null)
                {
                    foreach (var name in properties)
                    {
                        context.Entry(child).Property(name).IsModified = true;
                    }
                }
            }

            var foreignKey = ((Microsoft.EntityFrameworkCore.Metadata.INavigation)relationship.Metadata).ForeignKey;
            foreach (var child in additions)
            {
                // Ownership never comes from the request or a mapper. Bind every FK component to this principal.
                for (var index = 0; index < foreignKey.Properties.Count; index++)
                {
                    context.Entry(child).Property(foreignKey.Properties[index].Name).CurrentValue =
                        entry.Property(foreignKey.PrincipalKey.Properties[index].Name).CurrentValue;
                }

                collection.Add(child);
                context.Add(child);
            }

            if (deleteMissing)
            {
                foreach (var child in missing)
                {
                    context.Remove(child);
                    collection.Remove(child);
                }
            }

            return new(assigned, rows.Cast<object>().Concat(additions), Restore);
        }
        catch
        {
            Restore();
            throw;
        }
    }
}
