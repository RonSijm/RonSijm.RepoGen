using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Explicitly simulates a mapped generated scalar using the candidate and detached same-store rows.</summary>
    /// <remarks>Requires an entity-shaped or explicitly write-mapped public store and automatic test commands. Mapped insert values are allocated
    /// before Stage; mapped update values only during a successful Save preparation. Factories must be pure.
    /// Insert-only values preserve supplied values unequal to the model's portable scalar sentinel.
    /// Computed insert/update values are always generated; custom conversion/comparison is not inferred.
    /// This is not database identity, trigger, clock or race equivalence.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryGeneratedValue<TValue>(Expression<Func<TEntity, TValue>> property,
        Expression<Func<TEntity, IReadOnlyList<TEntity>, TValue>> value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        var name = GetProperty(property).Name;
        var store = RequireGeneratedValueStore();
        if (store.GeneratedValues.Any(item => item.PropertyName == name))
            throw new ArgumentException("RGD3501: A fixture generated-value adapter is already declared for " + name, nameof(property));
        store.GeneratedValuesList.Add(new(name, value));
        return this;
    }

    public RepositoryEntityBuilder<TEntity> WithoutInMemoryGeneratedValue<TValue>(Expression<Func<TEntity, TValue>> property)
    {
        var name = GetProperty(property).Name;
        if (design.InMemoryStore is { } store) store.GeneratedValuesList.RemoveAll(item => item.PropertyName == name);
        return this;
    }

    /// <summary>Explicitly supplies a generated scalar by its exact final-model name and CLR type.</summary>
    /// <remarks>Named field/indexer/shadow state additionally requires MapInMemoryWrites(useMappedPropertyState: true)
    /// and an exact MapInMemoryProperty binding. The pure factory is emitted, never evaluated during generation.
    /// This does not infer database token advancement, identity or provider behavior.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryGeneratedValue<TValue>(string propertyName,
        Expression<Func<TEntity, IReadOnlyList<TEntity>, TValue>> value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("RGD3501: An exact mapped property name is required.", nameof(propertyName));
        }
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
        var store = RequireGeneratedValueStore();
        if (store.GeneratedValues.Any(item => item.PropertyName == propertyName))
        {
            throw new ArgumentException("RGD3501: A fixture generated-value adapter is already declared for " + propertyName, nameof(propertyName));
        }
        store.GeneratedValuesList.Add(new(propertyName, value));
        return this;
    }

    public RepositoryEntityBuilder<TEntity> WithoutInMemoryGeneratedValue(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("RGD3501: An exact mapped property name is required.", nameof(propertyName));
        }
        if (design.InMemoryStore is { } store)
        {
            store.GeneratedValuesList.RemoveAll(item => item.PropertyName == propertyName);
        }
        return this;
    }

    private RepositoryInMemoryStoreDesign RequireGeneratedValueStore()
        => design.InMemoryStore is { } store && (store.RowType == typeof(TEntity) || store.WriteMapping is not null) ? store
            : throw new InvalidOperationException("RGD3501: Generated-value adapters require a preceding entity-shaped HasInMemoryStore or explicitly mapped public write store.");
}
