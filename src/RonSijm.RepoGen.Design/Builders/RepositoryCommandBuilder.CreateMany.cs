using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Maps bounded request items to new scalar rows. Choose Save or Stage explicitly.</summary>
    /// <remarks>One shared save, not a native bulk insert. Saved results follow input order.</remarks>
    public RepositoryCommandBuilder<TEntity> CreateMany<TItem, TRequest>(
        Expression<Func<TRequest, IEnumerable<TItem>>> items, Expression<Func<TItem, TEntity>> mapping,
        int maximumItems = 1000)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (mapping is null) throw new ArgumentNullException(nameof(mapping));
        if (design.CreateMany is not null || design.CreateMapping is not null || design.Batch is not null)
            throw new InvalidOperationException("RGD3801: A command can configure only one creation or matching-key batch mapping.");
        design.Kind = RepositoryCommandKind.Create;
        design.CreateMany = new(items, mapping, maximumItems);
        return this;
    }
}
