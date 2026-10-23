using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Configures one principal-key component's comparison in declared fixture-read relationships.</summary>
    /// <remarks>Opt-in and independent of store identity, query predicates and tracked writes. The comparer is
    /// constructed in generated test output, never during generation. All directions referencing this principal
    /// key use the same policy, including uniqueness checks and explicit many-to-many join pairs.
    /// Null FK components still never join. Converters and provider comparers remain unsupported.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryRelationshipComparer<TValue>(Expression<Func<TEntity, TValue>> property,
        Expression<Func<IEqualityComparer<TValue>>> comparer)
    {
        if (comparer is null) throw new ArgumentNullException(nameof(comparer));
        var name = GetProperty(property).Name;
        var store = design.InMemoryStore ?? throw new InvalidOperationException("RGD4501: Relationship comparison requires a preceding HasInMemoryStore.");
        if (store.RelationshipComparers.Any(value => value.PropertyName == name))
            throw new ArgumentException("RGD4501: Relationship comparison is already declared for " + name, nameof(property));
        store.RelationshipComparersList.Add(new(name, typeof(TValue), comparer));
        return this;
    }

    /// <summary>Removes the explicit component policy; structural relationship comparison applies again.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryRelationshipComparer<TValue>(Expression<Func<TEntity, TValue>> property)
    {
        var name = GetProperty(property).Name;
        design.InMemoryStore?.RelationshipComparersList.RemoveAll(value => value.PropertyName == name);
        return this;
    }
}
