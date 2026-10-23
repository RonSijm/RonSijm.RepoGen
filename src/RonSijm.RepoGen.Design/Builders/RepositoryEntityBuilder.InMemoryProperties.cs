using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Maps one exact model scalar to public fixture data for generated reads only.</summary>
    /// <remarks>Requires matching HasInMemoryStore and MapInMemoryEntity declarations. The final model
    /// validates the constant property name and exact value type. No getter/expression runs during
    /// generation. Supports shadow/field/indexer/ordinary properties without entity materialization.
    /// An initializer assignment to the same property is rejected rather than silently overridden.
    /// Model filters, navigation loading and writes retain their separate permissions;
    /// this does not simulate tracked state, versions, defaults or provider behavior.</remarks>
    public RepositoryEntityBuilder<TEntity> MapInMemoryProperty<TData, TProperty>(string propertyName,
        Expression<Func<TData, TProperty>> value) where TData : class
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("A model property name is required.", nameof(propertyName));
        }
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
        if (design.InMemoryStore is not { EntityMapping: not null } store || store.RowType != typeof(TData) || store.RowType == typeof(TEntity))
        {
            throw new InvalidOperationException("RGD4901: MapInMemoryProperty requires preceding matching public HasInMemoryStore and MapInMemoryEntity declarations.");
        }
        store.PropertyMappingsList.RemoveAll(property => property.PropertyName == propertyName);
        store.PropertyMappingsList.Add(new(propertyName, value));
        return this;
    }

    /// <summary>Removes one fixture-only scalar value without changing production metadata or scripts.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryProperty(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("A model property name is required.", nameof(propertyName));
        }
        design.InMemoryStore?.PropertyMappingsList.RemoveAll(property => property.PropertyName == propertyName);
        return this;
    }
}
