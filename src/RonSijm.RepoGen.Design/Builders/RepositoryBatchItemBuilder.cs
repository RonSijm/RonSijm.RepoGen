using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Explicit scalar assignments and original versions for each item of a named batch command.</summary>
public sealed class RepositoryBatchItemBuilder<TEntity, TItem> where TEntity : class
{
    private readonly RepositoryCommandDesign mapping;
    private readonly RepositoryUpdateBuilder<TEntity> updates;

    internal RepositoryBatchItemBuilder(RepositoryCommandDesign mapping)
    {
        this.mapping = mapping;
        updates = new(mapping);
    }

    public RepositoryBatchItemBuilder<TEntity, TItem> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TItem, TProperty>> value)
    {
        updates.SetProperty<TProperty, TItem>(property, value);
        return this;
    }

    public RepositoryBatchItemBuilder<TEntity, TItem> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TProperty>> value)
    {
        updates.SetProperty(property, value);
        return this;
    }

    public RepositoryBatchItemBuilder<TEntity, TItem> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property, TProperty value)
    {
        updates.SetProperty(property, value);
        return this;
    }

    public RepositoryBatchItemBuilder<TEntity, TItem> SetPropertyWhen<TProperty>(Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TItem, TProperty>> value, Expression<Func<TItem, bool>> condition)
    {
        updates.SetPropertyWhen(property, value, condition);
        return this;
    }

    public RepositoryBatchItemBuilder<TEntity, TItem> ExpectVersion<TProperty>(Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TItem, TProperty>> expected)
    {
        new RepositoryCommandBuilder<TEntity>(mapping).ExpectVersion(property, expected);
        return this;
    }
}
