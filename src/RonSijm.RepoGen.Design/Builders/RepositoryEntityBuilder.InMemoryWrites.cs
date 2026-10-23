using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Explicitly maps simulated scalar command state back to a public fixture row.</summary>
    /// <remarks>Requires a preceding matching public HasInMemoryStore and MapInMemoryEntity.
    /// The entity parameter represents a generated plain scalar row, not an instantiated persistence entity.
    /// The second parameter is the saved public row for updates, or null for creates. Preserve arrangement-only
    /// values deliberately. Maps must be pure, preserve keys and round-trip every mapped scalar.
    /// Automatic test commands are independently optional; no graph or reverse DTO mapping is inferred.
    /// useMappedPropertyState separately permits MapInMemoryProperty bindings for non-identity scalar
    /// state and original-version checks. Keys, unique-index members and foreign keys remain ordinary
    /// explicit assignments. Generated values still require explicit factories; this is not EF state simulation.</remarks>
    public RepositoryEntityBuilder<TEntity> MapInMemoryWrites<TData>(Expression<Func<TEntity, TData?, TData>> mapping,
        bool useMappedPropertyState = false) where TData : class
    {
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        if (design.InMemoryStore is not { EntityMapping: not null } store || store.RowType != typeof(TData) || store.RowType == typeof(TEntity))
            throw new InvalidOperationException("RGD3601: MapInMemoryWrites requires a preceding matching public HasInMemoryStore and MapInMemoryEntity.");
        store.WriteMapping = mapping;
        store.MappedPropertyStateEnabled = useMappedPropertyState;
        return this;
    }

    /// <summary>Removes automatic public-row writes and their value factories, retaining scalar reads and scripts.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryWrites()
    {
        if (design.InMemoryStore is { WriteMapping: not null } store)
        {
            store.WriteMapping = null;
            store.MappedPropertyStateEnabled = false;
            store.GeneratedValuesList.Clear();
            store.ModelDefaultsEnabled = false;
        }
        return this;
    }
}
