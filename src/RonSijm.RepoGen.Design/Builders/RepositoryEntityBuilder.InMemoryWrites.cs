using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Explicitly maps simulated scalar command state back to a public fixture row.</summary>
    /// <remarks>Requires a preceding matching public HasInMemoryStore and MapInMemoryEntity.
    /// The entity parameter represents a generated plain scalar row, not an instantiated persistence entity.
    /// The second parameter is the saved public row for updates, or null for creates. Preserve arrangement-only
    /// values deliberately. Maps must be pure, preserve keys and round-trip every mapped scalar.
    /// Automatic test commands are independently optional; no graph or reverse DTO mapping is inferred.</remarks>
    public RepositoryEntityBuilder<TEntity> MapInMemoryWrites<TData>(Expression<Func<TEntity, TData?, TData>> mapping) where TData : class
    {
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        if (design.InMemoryStore is not { EntityMapping: not null } store || store.RowType != typeof(TData) || store.RowType == typeof(TEntity))
            throw new InvalidOperationException("RGD3601: MapInMemoryWrites requires a preceding matching public HasInMemoryStore and MapInMemoryEntity.");
        store.WriteMapping = mapping;
        return this;
    }

    /// <summary>Removes automatic public-row writes and their value factories, retaining scalar reads and scripts.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryWrites()
    {
        if (design.InMemoryStore is { WriteMapping: not null } store)
        {
            store.WriteMapping = null;
            store.GeneratedValuesList.Clear();
            store.ModelDefaultsEnabled = false;
        }
        return this;
    }
}
