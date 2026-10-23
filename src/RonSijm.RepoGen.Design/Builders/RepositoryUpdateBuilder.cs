using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryUpdateBuilder<TEntity>
    where TEntity : class
{
    private readonly RepositoryCommandDesign design;

    internal RepositoryUpdateBuilder(RepositoryCommandDesign design)
    {
        this.design = design;
    }

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter1, TParameter2>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter1, TParameter2, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter1, TParameter2, TParameter3>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter1, TParameter2, TParameter3, TParameter4>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TParameter4, TProperty>> value)
        => AddUpdate(property, value);

    private RepositoryUpdateBuilder<TEntity> AddUpdate<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        LambdaExpression value)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        design.UpdatesList.Add(new RepositoryUpdateDesign(property, value));
        return this;
    }

    public RepositoryUpdateBuilder<TEntity> SetPropertyWhen<TProperty, TRequest>(Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TRequest, TProperty>> value, Expression<Func<TRequest, bool>> condition)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        design.UpdatesList.Add(new RepositoryUpdateDesign(property, value, condition));
        return this;
    }

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        TProperty value)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var constantValue = Expression.Constant(value, typeof(TProperty));
        var valueExpression = Expression.Lambda<Func<TEntity, TProperty>>(constantValue, parameter);
        return SetProperty(property, valueExpression);
    }
}
