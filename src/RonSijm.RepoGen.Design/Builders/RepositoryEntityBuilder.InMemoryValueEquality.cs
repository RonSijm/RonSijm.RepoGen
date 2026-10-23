using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Declares fixture-only scalar equality for tracked write changes and version checks.</summary>
    /// <remarks>Requires a preceding entity-shaped or explicitly write-mapped public store and the automatic command backend.
    /// The self-contained expression is emitted, never invoked during generation. Keys, unique-index members,
    /// foreign keys, generated values, conversions, query predicates and SQL comparison remain separate.</remarks>
    public RepositoryEntityBuilder<TEntity> HasInMemoryValueEquality<TValue>(Expression<Func<TEntity, TValue>> property,
        Expression<Func<TValue, TValue, bool>> equality)
    {
        if (equality is null) throw new ArgumentNullException(nameof(equality));
        var name = GetProperty(property).Name;
        var store = design.InMemoryStore is { } configured && (configured.RowType == typeof(TEntity) || configured.WriteMapping is not null)
            ? configured : throw new InvalidOperationException("RGD4101: Fixture value equality requires a preceding entity-shaped or explicitly write-mapped public store.");
        if (store.ValueEqualities.Any(value => value.PropertyName == name))
            throw new ArgumentException("RGD4101: Fixture value equality is already declared for " + name, nameof(property));
        store.ValueEqualitiesList.Add(new(name, equality));
        return this;
    }

    /// <summary>Removes this scalar's explicit fixture policy; ordinary structural equality applies again.</summary>
    public RepositoryEntityBuilder<TEntity> WithoutInMemoryValueEquality<TValue>(Expression<Func<TEntity, TValue>> property)
    {
        var name = GetProperty(property).Name;
        design.InMemoryStore?.ValueEqualitiesList.RemoveAll(value => value.PropertyName == name);
        return this;
    }
}
