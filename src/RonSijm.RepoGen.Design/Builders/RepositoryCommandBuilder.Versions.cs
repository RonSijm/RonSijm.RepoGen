using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Checks an exact typed mapped token, including shadow properties, using only caller input.</summary>
    /// <remarks>The name is a design-time binding, not a runtime query argument. Does not assign/increment
    /// the token or opt into shadow return values, fixture state, retries or conflict resolution.</remarks>
    public RepositoryCommandBuilder<TEntity> ExpectVersion<TProperty, TRequest>(string propertyName,
        Expression<Func<TRequest, TProperty>> expected)
    {
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        var value = Expression.Lambda(expected.Body, Expression.Parameter(typeof(TEntity), "entity"), expected.Parameters[0]);
        design.ExpectedVersionsList.Add(new RepositoryExpectedVersionDesign(propertyName, typeof(TProperty), value));
        return this;
    }
}
