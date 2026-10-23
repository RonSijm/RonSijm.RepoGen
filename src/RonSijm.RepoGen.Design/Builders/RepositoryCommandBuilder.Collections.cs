using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity>
{
    /// <summary>Reconciles explicitly mapped, owner-scoped leaf children of a tracked Patch command.</summary>
    /// <remarks>Nothing is inferred from a request graph. Add/update/delete are independently opt-in.
    /// Matching can select a mapped scalar or construct a ValueTuple of two to seven distinct,
    /// non-null mapped scalars. EF metadata must prove uniqueness globally or within this owner.</remarks>
    public RepositoryCommandBuilder<TEntity> ReconcileCollection<TChild, TItem, TKey, TRequest>(
        Expression<Func<TEntity, ICollection<TChild>>> navigation,
        Expression<Func<TRequest, IEnumerable<TItem>>> items,
        Expression<Func<TChild, TKey>> childKey,
        Expression<Func<TItem, TKey>> itemKey,
        Action<RepositoryCollectionReconciliationBuilder<TChild, TItem>> configure)
        where TChild : class
        where TKey : notnull
    {
        if (navigation is null) throw new ArgumentNullException(nameof(navigation));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (childKey is null) throw new ArgumentNullException(nameof(childKey));
        if (itemKey is null) throw new ArgumentNullException(nameof(itemKey));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var collection = new RepositoryCollectionReconciliationDesign(navigation, items, childKey, itemKey);
        configure(new RepositoryCollectionReconciliationBuilder<TChild, TItem>(collection));
        design.CollectionsList.Add(collection);
        if (design.Kind == RepositoryCommandKind.Unspecified)
        {
            design.Kind = RepositoryCommandKind.Patch;
        }

        return this;
    }
}
