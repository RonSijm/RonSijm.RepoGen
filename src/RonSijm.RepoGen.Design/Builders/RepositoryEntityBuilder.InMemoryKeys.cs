using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Declares explicit key identity for this test-only store.</summary>
    /// <remarks>Requires a preceding store with exactly the same key type. The self-contained factory
    /// is emitted, not executed during generation, and is evaluated once per context partition.
    /// Use an ordinal StringComparer, EqualityComparer&lt;TKey&gt;.Default, or construct an independent
    /// public comparer. Predicates, sorting, relationship matching and production collation are unchanged.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryKeyComparer<TKey>(Expression<Func<IEqualityComparer<TKey>>> comparer) where TKey : notnull
    {
        if (comparer is null) throw new ArgumentNullException(nameof(comparer));
        if (design.InMemoryStore is not { } store || store.KeyType != typeof(TKey))
            throw new InvalidOperationException("RGD3204: HasInMemoryKeyComparer requires a preceding HasInMemoryStore with exactly the same key type.");
        store.KeyComparer = comparer;
        return this;
    }

    /// <summary>Restores EqualityComparer&lt;TKey&gt;.Default without removing the store or its other adapters.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryKeyComparer()
    {
        if (design.InMemoryStore is { } store) store.KeyComparer = null;
        return this;
    }
}
