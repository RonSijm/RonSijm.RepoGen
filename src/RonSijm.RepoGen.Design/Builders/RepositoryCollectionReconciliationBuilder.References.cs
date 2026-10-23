using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCollectionReconciliationBuilder<TChild, TItem> where TChild : class
{
    /// <summary>Selects the required dependent reference whose complete FK defines link identity.</summary>
    /// <remarks>Required when a link has additional references; referenced objects are never written.</remarks>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> TargetReference<TReference>(
        Expression<Func<TChild, TReference?>> navigation) where TReference : class
    {
        RequireLinkReference(navigation);
        design.TargetReference = navigation;
        return this;
    }

    public RepositoryCollectionReconciliationBuilder<TChild, TItem> WithoutTargetReference()
    {
        design.TargetReference = null;
        return this;
    }

    /// <summary>Allows scalar initialization of another required FK on newly added links only.</summary>
    /// <remarks>Every component must be mapped explicitly. Updates cannot change this FK, and its
    /// existence/authorization is not inferred or enforced by fixtures. Shared columns need
    /// AllowSharedReferenceColumns; owner-shared values must agree before ownership is bound.</remarks>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> AdditionalReference<TReference>(
        Expression<Func<TChild, TReference?>> navigation) where TReference : class
        => AddReference(navigation, optional: false);

    /// <summary>Allows complete scalar initialization of an optional FK on new links only.</summary>
    /// <remarks>Independent components must be nullable and explicitly mapped: all null means absent,
    /// partial null is rejected. AllowSharedReferenceColumns separately admits owner-shared non-nullable
    /// components, checked before binding even when the reference is absent. Other optional overlaps
    /// remain unsupported. Matched references are never reassigned.</remarks>
    public RepositoryCollectionReconciliationBuilder<TChild, TItem> OptionalAdditionalReference<TReference>(
        Expression<Func<TChild, TReference?>> navigation) where TReference : class
        => AddReference(navigation, optional: true);

    private RepositoryCollectionReconciliationBuilder<TChild, TItem> AddReference(LambdaExpression navigation, bool optional)
    {
        var name = RequireLinkReference(navigation);
        if (design.AdditionalReferences.Any(value => ReferenceName(value.Navigation) == name))
            throw new ArgumentException("RGD4201: Additional link reference is already declared: " + name, nameof(navigation));
        design.AdditionalReferencesList.Add(new(navigation, optional));
        return this;
    }

    public RepositoryCollectionReconciliationBuilder<TChild, TItem> WithoutAdditionalReference<TReference>(
        Expression<Func<TChild, TReference?>> navigation) where TReference : class
    {
        var name = RequireLinkReference(navigation);
        design.AdditionalReferencesList.RemoveAll(value => ReferenceName(value.Navigation) == name);
        return this;
    }

    private string RequireLinkReference(LambdaExpression navigation)
    {
        if (navigation is null) throw new ArgumentNullException(nameof(navigation));
        if (!design.IsLinkReconciliation)
            throw new InvalidOperationException("RGD4201: Named reference roles are only supported by ReconcileLinks.");
        return ReferenceName(navigation);
    }

    private static string ReferenceName(LambdaExpression navigation)
    {
        Expression body = navigation.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion) body = conversion.Operand;
        if (body is not MemberExpression { Expression: ParameterExpression parameter } member || parameter != navigation.Parameters[0])
            throw new ArgumentException("RGD4201: Select one direct mapped dependent reference navigation.", nameof(navigation));
        return member.Member.Name;
    }
}
