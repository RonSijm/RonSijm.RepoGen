using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Prepares scalar insertions atomically, reserving explicit fixture values across the batch.</summary>
    public Task<CommandResult<IReadOnlyList<TResult>>> CreateManyAsync<TEntity, TItem, TKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, IReadOnlyList<TItem> input, Func<TItem, TEntity> factory,
        Func<TEntity, TResult> result, bool save, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
        => CreateManyCoreAsync(store, input, factory, result, save, null, cancellationToken);

    public Task<CommandResult<IReadOnlyList<TResult>>> CreateManyAsync<TEntity, TItem, TKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, IReadOnlyList<TItem> input, Func<TItem, TEntity> factory,
        Func<IReadOnlyList<TEntity>, IReadOnlyList<TResult>> savedResult, bool save, CancellationToken cancellationToken = default)
        where TEntity : class where TKey : notnull
        => CreateManyCoreAsync(store, input, factory, static _ => default!, save,
            savedResult ?? throw new ArgumentNullException(nameof(savedResult)), cancellationToken);

    private Task<CommandResult<IReadOnlyList<TResult>>> CreateManyCoreAsync<TEntity, TItem, TKey, TResult>(
        InMemoryWriteStore<TEntity, TKey> store, IReadOnlyList<TItem> input, Func<TItem, TEntity> factory,
        Func<TEntity, TResult> result, bool save, Func<IReadOnlyList<TEntity>, IReadOnlyList<TResult>>? savedResult,
        CancellationToken cancellationToken) where TEntity : class where TKey : notnull
        => Run(() =>
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(result);
            if (savedResult is not null && !save) throw new ArgumentException("A saved create-batch result requires Save.", nameof(savedResult));
            var prepared = Access(Prepare);
            if (prepared.Rows is null) return new CommandResult<IReadOnlyList<TResult>>(prepared.Status, prepared.Count);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<TResult> values;
                if (savedResult is not null)
                {
                    values = savedResult(prepared.Rows);
                    if (values is null || values.Count != prepared.Rows.Count)
                        throw new InvalidOperationException("A saved create-batch result must return exactly one value per input row.");
                }
                else
                {
                    var projected = new List<TResult>(prepared.Rows.Count);
                    foreach (var row in prepared.Rows) { cancellationToken.ThrowIfCancellationRequested(); projected.Add(result(row)); }
                    values = projected.AsReadOnly();
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new CommandResult<IReadOnlyList<TResult>>(CommandStatus.Applied, prepared.Count, values, hasValue: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            { throw new RepositorySavedResultException(exception); }

            (CommandStatus Status, int Count, IReadOnlyList<TEntity>? Rows) Prepare()
            {
                var state = Pending(store);
                if (input.Count == 0) return (CommandStatus.NoChanges, 0, null);
                var previous = state.Entries;
                var staged = new Dictionary<TKey, InMemoryPendingEntry<TEntity>>(previous, store.KeyComparer);
                var existing = store.CopyWriteRows();
                var reserved = new List<TEntity>(input.Count);
                var identities = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);
                foreach (var item in input)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item is null) throw new ArgumentException("Create batch items must be non-null.", nameof(input));
                    var candidate = factory(item) ?? throw new InvalidOperationException("The create mapping returned null.");
                    if (!identities.Add(candidate)) throw new InvalidOperationException("Each create-batch mapping must return a distinct row.");
                    var row = store.Detach(candidate);
                    state.PrepareCreate(row, reserved);
                    row = store.Detach(row);
                    var key = store.KeyOf(row);
                    if (key is null) throw new InvalidOperationException("The fixture key adapter returned null: " + store.Name);
                    if (existing.ContainsKey(key) || staged.ContainsKey(key)) throw new InvalidOperationException("Duplicate key in in-memory fixture store: " + store.Name);
                    staged.Add(key, new InMemoryPendingEntry<TEntity>
                    {
                        Current = row, SameValues = static (_, _) => true, SameTokens = static (_, _) => true, Merge = static (_, _, _) => { }
                    });
                    reserved.Add(row);
                }
                cancellationToken.ThrowIfCancellationRequested();
                state.ReplaceEntries(staged);
                try
                {
                    if (!save) return (CommandStatus.Staged, 0, null);
                    var rows = reserved.Select(store.Detach).ToArray();
                    var count = SaveCore(cancellationToken);
                    return (CommandStatus.Applied, count, Array.AsReadOnly(rows));
                }
                catch (InMemoryWriteConflictException exception) { state.ReplaceEntries(previous); throw new RepositoryWriteConflictException(exception); }
                catch { state.ReplaceEntries(previous); throw; }
            }
        }, cancellationToken);
}
