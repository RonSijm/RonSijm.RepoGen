using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Simulates a bounded designed patch/delete batch, with all-or-nothing staging and one shared save.</summary>
    /// <remarks>Matching uses ordinary CLR key equality. Targets with pending modifications must be saved/discarded first.</remarks>
    public Task<CommandResult<IReadOnlyList<TResult>>> ExecuteBatchAsync<TEntity, TItem, TKey, TMatchKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, RepositoryBatchInput<TItem, TMatchKey> input,
        Func<TEntity, bool> predicate, Func<TEntity, TMatchKey> matchKey,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch, Func<TEntity, TItem, bool> expected,
        Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<TEntity, TResult> result,
        bool delete, bool save, bool forceWrite = false, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull where TMatchKey : notnull
        => ExecuteBatchCoreAsync(store, input, predicate, matchKey, patch, expected, sameValues, sameTokens, merge,
            result, delete, save, forceWrite, null, cancellationToken);

    /// <summary>Preserves equivalent field representations during non-forced mixed batch updates.</summary>
    public Task<CommandResult<IReadOnlyList<TResult>>> ExecuteBatchAsync<TEntity, TItem, TKey, TMatchKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, RepositoryBatchInput<TItem, TMatchKey> input,
        Func<TEntity, bool> predicate, Func<TEntity, TMatchKey> matchKey,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch, Func<TEntity, TItem, bool> expected,
        Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<TEntity, TResult> result,
        bool delete, bool save, bool forceWrite, Func<TEntity, TEntity, string, bool>? sameAssignedValue,
        CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull where TMatchKey : notnull
        => ExecuteBatchCoreAsync(store, input, predicate, matchKey, patch, expected, sameValues, sameTokens, merge,
            result, delete, save, forceWrite, null, cancellationToken, sameAssignedValue);

    private Task<CommandResult<IReadOnlyList<TResult>>> ExecuteBatchCoreAsync<TEntity, TItem, TKey, TMatchKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, RepositoryBatchInput<TItem, TMatchKey> input,
        Func<TEntity, bool> predicate, Func<TEntity, TMatchKey> matchKey,
        Func<TEntity, TItem, IReadOnlyList<string>>? patch, Func<TEntity, TItem, bool> expected,
        Func<TEntity, TEntity, bool> sameValues, Func<TEntity, TEntity, bool> sameTokens,
        Action<TEntity, TEntity, IReadOnlyCollection<string>> merge, Func<TEntity, TResult> result,
        bool delete, bool save, bool forceWrite, Func<IReadOnlyList<TEntity>, IReadOnlyList<TResult>>? savedResult,
        CancellationToken cancellationToken, Func<TEntity, TEntity, string, bool>? sameAssignedValue = null)
        where TEntity : class where TKey : notnull where TMatchKey : notnull
        => Run(() =>
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(matchKey);
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(sameValues);
            ArgumentNullException.ThrowIfNull(sameTokens);
            ArgumentNullException.ThrowIfNull(merge);
            ArgumentNullException.ThrowIfNull(result);
            if (savedResult is not null && (!save || delete))
                throw new ArgumentException("A saved batch result requires a saving patch, not staging or deletion.", nameof(savedResult));
            if (!delete) ArgumentNullException.ThrowIfNull(patch);
            var prepared = Access(Prepare);
            if (prepared.Rows is null) return new CommandResult<IReadOnlyList<TResult>>(prepared.Status, prepared.Count);
            try
            {
                if (savedResult is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var savedValues = savedResult(prepared.Rows);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (savedValues is null || savedValues.Count != prepared.Rows.Count)
                        throw new InvalidOperationException("A saved batch result must return exactly one value per saved input row.");
                    return new CommandResult<IReadOnlyList<TResult>>(CommandStatus.Applied, prepared.Count, savedValues, hasValue: true);
                }
                var values = new List<TResult>(prepared.Rows.Count);
                foreach (var row in prepared.Rows) { cancellationToken.ThrowIfCancellationRequested(); values.Add(result(row)); }
                return new CommandResult<IReadOnlyList<TResult>>(CommandStatus.Applied, prepared.Count, values.AsReadOnly(), hasValue: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            { throw new RepositorySavedResultException(exception); }

            (CommandStatus Status, int Count, IReadOnlyList<TEntity>? Rows) Prepare()
            {
                var state = Pending(store);
                if (input.Items.Count == 0) return (CommandStatus.NoChanges, 0, null);
                var keys = input.Keys.ToHashSet();
                var rows = store.CopyWriteRows().Values.Where(predicate).Where(row => keys.Contains(matchKey(row))).ToDictionary(matchKey);
                cancellationToken.ThrowIfCancellationRequested();
                if (rows.Count != input.Items.Count || input.Keys.Any(key => !rows.ContainsKey(key))) return (CommandStatus.NotFound, 0, null);
                var previous = state.Entries;
                foreach (var row in rows.Values)
                    if (previous.TryGetValue(store.KeyOf(row), out var pendingRow) && (pendingRow.Original is null || pendingRow.Deleted ||
                        pendingRow.ForceWrite || !pendingRow.SameValues(pendingRow.Original, pendingRow.Current)))
                        throw new InvalidOperationException("Batch target rows have pending changes. Save or discard those changes before executing the batch.");
                var staged = new Dictionary<TKey, InMemoryPendingEntry<TEntity>>(previous, store.KeyComparer);
                var entries = new List<InMemoryPendingEntry<TEntity>>();
                var results = new List<TEntity>(input.Items.Count);
                var resultEntries = new List<InMemoryPendingEntry<TEntity>>(input.Items.Count);
                var assignedAny = false;
                foreach (var (itemKey, item) in input.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = rows[itemKey];
                    var key = store.KeyOf(row);
                    var entry = previous.TryGetValue(key, out var existing) ? existing.Clone(store.Detach) : new InMemoryPendingEntry<TEntity>
                    {
                        Current = store.Detach(row), Original = store.Detach(row), SameValues = sameValues, SameTokens = sameTokens, Merge = merge, SameAssignedValue = sameAssignedValue
                    };
                    if (!expected(entry.Original!, item)) return (CommandStatus.Conflict, 0, null);
                    var assigned = delete ? Array.Empty<string>() : patch!(entry.Current, item);
                    if (!store.KeyComparer.Equals(key, store.KeyOf(entry.Current)) || !EqualityComparer<TMatchKey>.Default.Equals(itemKey, matchKey(entry.Current)))
                        throw new InvalidOperationException("A simulated batch changed a fixture/matching key: " + store.Name);
                    entry.Current = store.Detach(entry.Current);
                    results.Add(store.Detach(entry.Current));
                    resultEntries.Add(entry);
                    if (!delete && assigned.Count == 0) continue;
                    assignedAny = true;
                    entry.Deleted = delete;
                    entry.ForceWrite |= delete || forceWrite;
                    entry.Assigned.UnionWith(assigned);
                    staged[key] = entry;
                    entries.Add(entry);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!assignedAny) return (CommandStatus.NoChanges, 0, null);
                state.ReplaceEntries(staged);
                if (!save) return (CommandStatus.Staged, 0, null);
                try
                {
                    var generated = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
                    var count = SaveCore(cancellationToken, generated);
                    for (var index = 0; index < results.Count; index++)
                        if (generated.TryGetValue(resultEntries[index], out var value)) results[index] = (TEntity)value;
                    return (CommandStatus.Applied, count, results.AsReadOnly());
                }
                catch (Exception exception)
                {
                    state.ReplaceEntries(previous);
                    if (exception is InMemoryWriteConflictException conflict)
                    {
                        if (entries.Any(entry => ReferenceEquals(entry, conflict.Entry))) return (CommandStatus.Conflict, 0, null);
                        throw new RepositoryWriteConflictException(conflict);
                    }
                    throw;
                }
            }
        }, cancellationToken);
}
