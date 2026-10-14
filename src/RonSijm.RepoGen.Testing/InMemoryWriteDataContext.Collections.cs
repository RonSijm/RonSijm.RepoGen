using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Composes an explicitly bounded leaf-child write against a separate saved fixture store.</summary>
    /// <remarks>Ownership comes from the selected parent, never the input. Matching uses default CLR equality.
    /// Pending owner-scoped children must be saved/discarded first. Delegates must be pure and non-reentrant.</remarks>
    public InMemoryCollectionWrite<TParent> ReconcileCollection<TParent, TChild, TItem, TKey, TMatchKey>(
        InMemoryWriteStore<TChild, TKey> store, RepositoryBatchInput<TItem, TMatchKey>? input,
        Func<TParent, TChild, bool> belongsTo, Func<TChild, bool> visible, Func<TChild, TMatchKey> matchKey,
        Func<TItem, TChild>? create, Action<TParent, TChild> bindOwner,
        Func<TChild, TItem, IReadOnlyList<string>>? update, Func<TChild, TItem, bool> expected,
        Func<TChild, TChild, bool> sameValues, Func<TChild, TChild, bool> sameTokens,
        Action<TChild, TChild, IReadOnlyCollection<string>> merge,
        int maximumItems, bool deleteMissing, bool rejectMissing, bool forceWrite = false,
        Func<TChild, TChild, string, bool>? sameAssignedValue = null,
        Func<TParent, TMatchKey, bool>? matchesOwner = null, bool prepareCreateBeforeVersions = false)
        where TParent : class where TChild : class where TKey : notnull where TMatchKey : notnull
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(belongsTo);
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(matchKey);
        ArgumentNullException.ThrowIfNull(bindOwner);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(sameValues);
        ArgumentNullException.ThrowIfNull(sameTokens);
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumItems, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumItems, int.MaxValue - 1);
        if (input?.Items.Count > maximumItems) throw new ArgumentException("Collection input exceeds its item limit.", nameof(input));
        return new(this, Apply);

        InMemoryCollectionChanges Apply(TParent parent, CancellationToken cancellationToken)
        {
            if (input is null) return InMemoryCollectionChanges.None;
            cancellationToken.ThrowIfCancellationRequested();
            if (matchesOwner is not null) RepositoryReferenceKeys.RequireOwner(parent, input.Keys, matchesOwner, cancellationToken);
            var state = Pending(store);
            var previous = state.Entries;
            var all = store.CopyWriteRows();
            var rows = all.Values.Where(row => belongsTo(parent, row) && visible(row)).Take(maximumItems + 1).ToArray();
            if (rows.Length > maximumItems) throw new InvalidOperationException("Existing collection exceeds its item limit: " + store.Name);
            // Conservatively include hidden and newly staged owner-scoped children in the dirty-target check.
            if (previous.Values.Any(entry => (belongsTo(parent, entry.Current) || entry.Original is not null && belongsTo(parent, entry.Original)) &&
                (entry.Original is null || entry.Deleted || entry.ForceWrite || !entry.SameValues(entry.Original, entry.Current))))
                throw new InvalidOperationException("Collection has pending changes. Save or discard them before reconciling it: " + store.Name);
            var existing = rows.ToDictionary(matchKey);
            var inputKeys = input.Keys.ToHashSet();
            var missing = rows.Where(row => !inputKeys.Contains(matchKey(row))).ToArray();
            if (rejectMissing && missing.Length != 0) throw new ArgumentException("Collection does not permit missing existing keys: " + store.Name);
            // Optional composite link initializers enforce all-or-none FK absence. Production
            // prepares all additions before checking matched-row versions; preserve that failure
            // precedence without re-evaluating initializers or reserving generated IDs early.
            Dictionary<TMatchKey, TChild>? prepared = null;
            if (prepareCreateBeforeVersions)
            {
                prepared = [];
                foreach (var (key, item) in input.Items.Where(item => !existing.ContainsKey(item.Key)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (create is null) throw new ArgumentException("Collection does not permit new or unknown keys: " + store.Name);
                    var row = store.Detach(create(item) ?? throw new InvalidOperationException("The child create mapping returned null."));
                    if (!EqualityComparer<TMatchKey>.Default.Equals(key, matchKey(row)))
                        throw new InvalidOperationException("A link create mapping must preserve its supplied reference key: " + store.Name);
                    prepared.Add(key, row);
                }
            }
            foreach (var (key, item) in input.Items)
                if (existing.TryGetValue(key, out var row) && !expected(previous.TryGetValue(store.KeyOf(row), out var entry) ? entry.Original! : row, item))
                    throw new InMemoryCollectionConflictException();

            var staged = new Dictionary<TKey, InMemoryPendingEntry<TChild>>(previous, store.KeyComparer);
            var entries = new List<object>();
            // Let explicit create factories reserve earlier additions from this same reconciliation.
            state.ReplaceEntries(staged);
            try
            {
                foreach (var (key, item) in input.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!existing.TryGetValue(key, out var row))
                    {
                        if (create is null) throw new ArgumentException("Collection does not permit new or unknown keys: " + store.Name);
                        row = store.Detach(prepared is not null ? prepared[key] : create(item) ?? throw new InvalidOperationException("The child create mapping returned null."));
                        if (matchesOwner is not null && !EqualityComparer<TMatchKey>.Default.Equals(key, matchKey(row)))
                            throw new InvalidOperationException("A link create mapping must preserve its supplied reference key: " + store.Name);
                        bindOwner(parent, row);
                        state.PrepareCreate(row);
                        row = store.Detach(row);
                        if (!belongsTo(parent, row) || !EqualityComparer<TMatchKey>.Default.Equals(key, matchKey(row)))
                            throw new InvalidOperationException("A child create mapping/factory changed its ownership or matching key: " + store.Name);
                        var storeKey = store.KeyOf(row);
                        if (storeKey is null || all.ContainsKey(storeKey) || staged.ContainsKey(storeKey))
                            throw new InvalidOperationException("Duplicate or null key in in-memory fixture store: " + store.Name);
                        var added = new InMemoryPendingEntry<TChild> { Current = row, SameValues = sameValues, SameTokens = sameTokens, Merge = merge, SameAssignedValue = sameAssignedValue };
                        staged.Add(storeKey, added);
                        entries.Add(added);
                        continue;
                    }
                    if (update is null) continue;
                    var fixtureKey = store.KeyOf(row);
                    var changed = previous.TryGetValue(fixtureKey, out var prior) ? prior.Clone(store.Detach) : new InMemoryPendingEntry<TChild>
                    {
                        Current = store.Detach(row), Original = store.Detach(row), SameValues = sameValues, SameTokens = sameTokens, Merge = merge, SameAssignedValue = sameAssignedValue
                    };
                    var assigned = update(changed.Current, item);
                    if (!store.KeyComparer.Equals(fixtureKey, store.KeyOf(changed.Current)) || !belongsTo(parent, changed.Current) ||
                        !EqualityComparer<TMatchKey>.Default.Equals(key, matchKey(changed.Current)))
                        throw new InvalidOperationException("A simulated collection update changed a fixture/matching key or ownership: " + store.Name);
                    if (assigned.Count == 0) continue;
                    changed.Assigned.UnionWith(assigned);
                    changed.ForceWrite |= forceWrite;
                    changed.Current = store.Detach(changed.Current);
                    staged[fixtureKey] = changed;
                    entries.Add(changed);
                }
                if (deleteMissing)
                    foreach (var row in missing)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var key = store.KeyOf(row);
                        var removed = previous.TryGetValue(key, out var prior) ? prior.Clone(store.Detach) : new InMemoryPendingEntry<TChild>
                        {
                            Current = store.Detach(row), Original = store.Detach(row), SameValues = sameValues, SameTokens = sameTokens, Merge = merge, SameAssignedValue = sameAssignedValue
                        };
                        removed.Deleted = removed.ForceWrite = true;
                        staged[key] = removed;
                        entries.Add(removed);
                    }
                cancellationToken.ThrowIfCancellationRequested();
                return new(entries.Count != 0, entries.AsReadOnly(), () => state.ReplaceEntries(previous));
            }
            catch { state.ReplaceEntries(previous); throw; }
        }
    }

    /// <summary>Stages an approved parent patch and child-store reconciliations together, optionally saving the shared fixture unit of work.</summary>
    /// <remarks>Results are scalar parent projections. Entity graphs, provider constraints and SQL concurrency are not simulated.</remarks>
    public Task<CommandResult<TResult>> ReconcileAsync<TParent, TKey, TResult>(InMemoryWriteStore<TParent, TKey> store,
        Func<TParent, bool> predicate, Func<TParent, IReadOnlyList<string>> patch,
        IReadOnlyList<InMemoryCollectionWrite<TParent>> collections, Func<TParent, bool> expected,
        Func<TParent, TParent, bool> sameValues, Func<TParent, TParent, bool> sameTokens,
        Action<TParent, TParent, IReadOnlyCollection<string>> merge, Func<TParent, TResult> result,
        bool save, bool forceWrite = false, CancellationToken cancellationToken = default) where TParent : class where TKey : notnull
        => ReconcileAsync(store, predicate, patch, collections, expected, sameValues, sameTokens, merge, result,
            save, forceWrite, null, cancellationToken);

    /// <summary>Uses explicit field equality for the parent while retaining independent child policies and force flags.</summary>
    public Task<CommandResult<TResult>> ReconcileAsync<TParent, TKey, TResult>(InMemoryWriteStore<TParent, TKey> store,
        Func<TParent, bool> predicate, Func<TParent, IReadOnlyList<string>> patch,
        IReadOnlyList<InMemoryCollectionWrite<TParent>> collections, Func<TParent, bool> expected,
        Func<TParent, TParent, bool> sameValues, Func<TParent, TParent, bool> sameTokens,
        Action<TParent, TParent, IReadOnlyCollection<string>> merge, Func<TParent, TResult> result,
        bool save, bool forceWrite, Func<TParent, TParent, string, bool>? sameAssignedValue,
        CancellationToken cancellationToken = default) where TParent : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(collections);
        return Execute(store, predicate, null, patch, expected, sameValues, sameTokens, merge, result, false, save, forceWrite,
            cancellationToken, collections.ToArray(), sameAssignedValue);
    }
}
