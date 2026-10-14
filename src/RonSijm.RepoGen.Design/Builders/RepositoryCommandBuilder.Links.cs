using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity>
{
    /// <summary>Reconciles explicit join rows, not the related entities or a skip-navigation graph.</summary>
    /// <remarks>The navigation must select an ordinary one-to-many collection of keyed link rows.
    /// Its owner FK and one other required FK are disjoint unless AllowSharedReferenceColumns is explicitly configured.
    /// Shared components must agree with the selected owner. Matching must select the complete
    /// other FK, in mapped order. Add maps that reference key and payload; ownership is supplied
    /// from the selected parent. Update cannot change either FK. Keep is the default missing policy.
    /// Database FK constraints remain authoritative; no related authorization or creation is inferred.</remarks>
    public RepositoryCommandBuilder<TEntity> ReconcileLinks<TLink, TItem, TKey, TRequest>(
        Expression<Func<TEntity, ICollection<TLink>>> navigation,
        Expression<Func<TRequest, IEnumerable<TItem>>> items,
        Expression<Func<TLink, TKey>> linkKey,
        Expression<Func<TItem, TKey>> itemKey,
        Action<RepositoryCollectionReconciliationBuilder<TLink, TItem>> configure)
        where TLink : class where TKey : notnull
    {
        if (navigation is null) throw new ArgumentNullException(nameof(navigation));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (linkKey is null) throw new ArgumentNullException(nameof(linkKey));
        if (itemKey is null) throw new ArgumentNullException(nameof(itemKey));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var collection = new RepositoryCollectionReconciliationDesign(navigation, items, linkKey, itemKey) { IsLinkReconciliation = true };
        configure(new RepositoryCollectionReconciliationBuilder<TLink, TItem>(collection));
        design.CollectionsList.Add(collection);
        if (design.Kind == RepositoryCommandKind.Unspecified) design.Kind = RepositoryCommandKind.Patch;
        return this;
    }
}
