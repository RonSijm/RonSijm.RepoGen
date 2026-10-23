using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryUpdateBuilder<TEntity> where TEntity : class
{
    /// <summary>Binds a setter to an exact final-model scalar name and CLR type, without a public entity setter.</summary>
    /// <remarks>Only tracked Patch and ordinary set-based Update are supported. Identity, relationship and
    /// generated targets remain excluded. Names are design-time metadata, never service inputs.</remarks>
    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty>(string propertyName, Expression<Func<TEntity, TProperty>> value)
        => AddMappedUpdate<TProperty>(propertyName, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TRequest>(string propertyName, Expression<Func<TEntity, TRequest, TProperty>> value)
        => AddMappedUpdate<TProperty>(propertyName, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty>(string propertyName, TProperty value)
        => AddMappedUpdate<TProperty>(propertyName, Expression.Lambda<Func<TEntity, TProperty>>(
            Expression.Constant(value, typeof(TProperty)), Expression.Parameter(typeof(TEntity), "entity")));

    /// <summary>Conditionally assigns mapped scalar state during a tracked Patch.</summary>
    public RepositoryUpdateBuilder<TEntity> SetPropertyWhen<TProperty, TRequest>(string propertyName,
        Expression<Func<TEntity, TRequest, TProperty>> value, Expression<Func<TRequest, bool>> condition)
    {
        if (condition is null) throw new ArgumentNullException(nameof(condition));
        return AddMappedUpdate<TProperty>(propertyName, value, condition);
    }

    private RepositoryUpdateBuilder<TEntity> AddMappedUpdate<TProperty>(string propertyName, LambdaExpression value, LambdaExpression? condition = null)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        design.UpdatesList.Add(new(propertyName, typeof(TProperty), value, condition));
        return this;
    }
}
