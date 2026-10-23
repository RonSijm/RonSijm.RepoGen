using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCollectionReconciliationBuilder<TChild, TItem> where TChild : class
{
    private readonly RepositoryCollectionReconciliationDesign design;

    internal RepositoryCollectionReconciliationBuilder(RepositoryCollectionReconciliationDesign design) => this.design = design;

    /// <summary>Allows link-reference columns to overlap; optional reference overlaps must be owner-shared.</summary>
    /// <remarks>Off by default and only valid for ReconcileLinks. Matching still selects the complete target FK;
    /// owner-shared keys/creation values must agree with the selected owner before binding. Shared columns
    /// cannot be updated or used to transfer links. Optional additional references may share owner
    /// components only; independent components retain their explicit nullable absence policy.</remarks>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> AllowSharedReferenceColumns(bool enabled = true)
    {
        if (enabled && !design.IsLinkReconciliation)
            throw new InvalidOperationException("RGD3902: Shared reference columns are only supported by explicit ReconcileLinks.");
        design.SharedReferenceColumnsEnabled = enabled;
        return this;
    }

    /// <summary>Allows new keys and explicitly maps their scalar fields. The generator supplies ownership.</summary>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> Add(Expression<Func<TItem, TChild>> mapping)
    {
        design.CreateMapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        return this;
    }

    public RepositoryCollectionReconciliationBuilder<TChild, TItem> Update(Action<RepositoryCollectionUpdateBuilder<TChild, TItem>> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        configure(new RepositoryCollectionUpdateBuilder<TChild, TItem>(design.Writes));
        return this;
    }

    public RepositoryCollectionReconciliationBuilder<TChild, TItem> AllowWrite<TProperty>(Expression<Func<TChild, TProperty>> property)
    {
        design.Writes.AllowedWritesList.Add(property ?? throw new ArgumentNullException(nameof(property)));
        return this;
    }

    public RepositoryCollectionReconciliationBuilder<TChild, TItem> ExpectVersion<TProperty>(
        Expression<Func<TChild, TProperty>> property, Expression<Func<TChild, TItem, TProperty>> expected)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        design.Writes.ExpectedVersionsList.Add(new RepositoryExpectedVersionDesign(property, expected));
        return this;
    }

    /// <summary>Skips this collection entirely when the request condition is false (e.g. an omitted Optional value).</summary>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> When<TRequest>(Expression<Func<TRequest, bool>> condition)
    {
        design.Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        return this;
    }

    /// <summary>Bounds both supplied items and existing visible children. Oversized collections fail without writing.</summary>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> MaximumItems(int maximum)
    {
        if (maximum < 1 || maximum == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximum));
        design.MaximumItems = maximum;
        return this;
    }

    /// <summary>Keep is the default. Delete is a deliberate tracked deletion, never an inferred orphan policy.</summary>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> MissingItems(RepositoryCollectionRemovalBehavior behavior)
    {
        if (!Enum.IsDefined(typeof(RepositoryCollectionRemovalBehavior), behavior))
        {
            throw new ArgumentOutOfRangeException(nameof(behavior));
        }

        design.MissingItems = behavior;
        return this;
    }
}
