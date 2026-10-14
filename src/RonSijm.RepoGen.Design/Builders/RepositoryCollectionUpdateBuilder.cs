using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Scalar child setters with the input item type fixed by ReconcileCollection.</summary>
public sealed class RepositoryCollectionUpdateBuilder<TChild, TItem> where TChild : class
{
    private readonly RepositoryUpdateBuilder<TChild> updates;

    internal RepositoryCollectionUpdateBuilder(RepositoryCommandDesign design) => updates = new(design);

    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetProperty<TProperty>(
        Expression<Func<TChild, TProperty>> property, Expression<Func<TChild, TItem, TProperty>> value)
    {
        updates.SetProperty<TProperty, TItem>(property, value);
        return this;
    }

    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetProperty<TProperty>(
        Expression<Func<TChild, TProperty>> property, Expression<Func<TChild, TProperty>> value)
    {
        updates.SetProperty(property, value);
        return this;
    }

    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetProperty<TProperty>(
        Expression<Func<TChild, TProperty>> property, TProperty value)
    {
        updates.SetProperty(property, value);
        return this;
    }

    public RepositoryCollectionUpdateBuilder<TChild, TItem> SetPropertyWhen<TProperty>(
        Expression<Func<TChild, TProperty>> property, Expression<Func<TChild, TItem, TProperty>> value,
        Expression<Func<TItem, bool>> condition)
    {
        updates.SetPropertyWhen(property, value, condition);
        return this;
    }
}
