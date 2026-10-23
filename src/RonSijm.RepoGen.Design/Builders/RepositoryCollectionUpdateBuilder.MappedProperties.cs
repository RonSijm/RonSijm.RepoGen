using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCollectionUpdateBuilder<TChild, TItem> where TChild : class
{
    /// <summary>Assigns an exact typed model scalar on matched children, without a public CLR setter.</summary>
    /// <remarks>Names are designer metadata. Matching/identity, ownership, relationship and generated targets remain excluded.</remarks>
    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetProperty<TProperty>(string propertyName,
        Expression<Func<TChild, TItem, TProperty>> value)
    {
        updates.SetProperty<TProperty, TItem>(propertyName, value);
        return this;
    }

    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetProperty<TProperty>(string propertyName,
        Expression<Func<TChild, TProperty>> value)
    {
        updates.SetProperty(propertyName, value);
        return this;
    }

    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetProperty<TProperty>(string propertyName, TProperty value)
    {
        updates.SetProperty(propertyName, value);
        return this;
    }

    /// <summary>Conditionally assigns matched-child scalar state using an item-only condition.</summary>
    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetPropertyWhen<TProperty>(string propertyName,
        Expression<Func<TChild, TItem, TProperty>> value, Expression<Func<TItem, bool>> condition)
    {
        updates.SetPropertyWhen(propertyName, value, condition);
        return this;
    }
}
