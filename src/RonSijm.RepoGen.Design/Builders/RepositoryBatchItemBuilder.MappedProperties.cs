using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryBatchItemBuilder<TEntity, TItem> where TEntity : class
{
    /// <summary>Assigns an exact typed model scalar in each bounded PatchMany item, without requiring a public CLR setter.</summary>
    /// <remarks>Names are designer metadata, not service inputs. Matching/identity, relationship and generated targets remain excluded.</remarks>
    public RepositoryBatchItemBuilder<TEntity, TItem> SetProperty<TProperty>(string propertyName,
        Expression<Func<TEntity, TItem, TProperty>> value)
    {
        updates.SetProperty<TProperty, TItem>(propertyName, value);
        return this;
    }

    public RepositoryBatchItemBuilder<TEntity, TItem> SetProperty<TProperty>(string propertyName,
        Expression<Func<TEntity, TProperty>> value)
    {
        updates.SetProperty(propertyName, value);
        return this;
    }

    public RepositoryBatchItemBuilder<TEntity, TItem> SetProperty<TProperty>(string propertyName, TProperty value)
    {
        updates.SetProperty(propertyName, value);
        return this;
    }

    /// <summary>Conditionally assigns mapped scalar state using a condition on the current request item.</summary>
    public RepositoryBatchItemBuilder<TEntity, TItem> SetPropertyWhen<TProperty>(string propertyName,
        Expression<Func<TEntity, TItem, TProperty>> value, Expression<Func<TItem, bool>> condition)
    {
        updates.SetPropertyWhen(propertyName, value, condition);
        return this;
    }
}
