using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Selects this entity for test-double generation. Null inherits the model setting.</summary>
    /// <remarks>Requires UseScriptedTestDoubles and closed repositories. Disabling never removes production operations.</remarks>
    public RepositoryEntityBuilder<TEntity> GenerateTestDouble(bool? enabled = true)
    {
        design.TestDoubleEnabled = enabled;
        return this;
    }

    /// <summary>Declares a test-only entity store with immutable keys and a detached-copy adapter.</summary>
    /// <remarks>Requires UseInMemoryTestStores to emit storage. All nested mutable values must be copied explicitly.
    /// Rows use consumer-supplied keys; this does not infer database-generated values or execute designer operations.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryStore<TKey>(Expression<Func<TEntity, TKey>> keySelector,
        Expression<Func<TEntity, TEntity>> copy) where TKey : notnull
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        if (copy is null) throw new ArgumentNullException(nameof(copy));
        design.InMemoryStore = new(typeof(TEntity), typeof(TKey), keySelector, copy);
        return this;
    }

    /// <summary>Uses an explicit public storage shape instead of exposing the persistence entity in testing output.</summary>
    /// <remarks>Useful for internal database models. Add MapInMemoryEntity to adapt scalar reads;
    /// without that explicit mapping, operation behavior requires scripts. Add MapInMemoryWrites separately for scalar writes.
    /// Keys and nested copies have the same rules as entity-shaped stores.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryStore<TData, TKey>(Expression<Func<TData, TKey>> keySelector,
        Expression<Func<TData, TData>> copy) where TData : class where TKey : notnull
    {
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        if (copy is null) throw new ArgumentNullException(nameof(copy));
        design.InMemoryStore = new(typeof(TData), typeof(TKey), keySelector, copy);
        return this;
    }

    /// <summary>Maps public fixture rows to explicitly selected EF scalar members for portable test reads.</summary>
    /// <remarks>Requires a matching HasInMemoryStore&lt;TData, TKey&gt; declaration. Use a parameterless entity
    /// initializer containing scalar assignments only. Generated test code uses a plain internal read row;
    /// it never constructs the persistence entity or executes its constructors/setters. Every scalar used by
    /// a query or visibility/default-sort policy must be mapped. Navigation slots and scalar writes require
    /// their own explicit HasInMemoryNavigation/MapInMemoryWrites declarations; identity/behavior is not adapted.</remarks>
    public RepositoryEntityBuilder<TEntity> MapInMemoryEntity<TData>(Expression<Func<TData, TEntity>> mapping) where TData : class
    {
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        if (design.InMemoryStore is not { } store || store.RowType != typeof(TData) || store.RowType == typeof(TEntity))
            throw new InvalidOperationException("RGD3203: MapInMemoryEntity requires a preceding HasInMemoryStore<TData, TKey> with a distinct, matching public data shape.");
        store.EntityMapping = mapping;
        return this;
    }

    /// <summary>Removes scalar read adaptation and dependent shadow/write/value adapters, retaining the public fixture store and scripts.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryEntityMapping()
    {
        if (design.InMemoryStore is { } store)
        {
            store.EntityMapping = null;
            store.PropertyMappingsList.Clear();
            store.MappedPropertyStateEnabled = false;
            if (store.WriteMapping is not null) { store.WriteMapping = null; store.GeneratedValuesList.Clear(); store.ModelDefaultsEnabled = false; }
        }
        return this;
    }

    /// <summary>Removes this entity's store declaration without disabling its scripted test double.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryStore()
    {
        design.InMemoryStore = null;
        return this;
    }
}
