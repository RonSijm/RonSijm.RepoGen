namespace RonSijm.RepoGen.Testing;

public partial class InMemoryWriteDataContext
{
    /// <summary>Registers pure, non-reentrant fixture-only value factories once, before staging.</summary>
    /// <remarks>Creation receives saved and reserved pending rows; update receives same-store save candidates.
    /// All arguments are detached. Callbacks must not mutate external state. Rollback/reset restore rows,
    /// not external counters. Generator-emitted callbacks assign only explicitly declared scalars.</remarks>
    public void ConfigureGeneratedValues<TEntity, TKey>(InMemoryWriteStore<TEntity, TKey> store,
        Action<TEntity, IReadOnlyList<TEntity>>? onCreate = null, Action<TEntity, IReadOnlyList<TEntity>>? onUpdate = null)
        where TEntity : class where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(store);
        if (onCreate is null && onUpdate is null) throw new ArgumentException("At least one generated-value callback is required.");
        Access(() =>
        {
            EnsureResetAllowed();
            var state = Pending(store);
            if (state.GenerateOnCreate is not null || state.GenerateOnUpdate is not null)
                throw new InvalidOperationException("Generated-value callbacks are already configured: " + store.Name);
            state.GenerateOnCreate = onCreate;
            state.GenerateOnUpdate = onUpdate;
        });
    }
}
