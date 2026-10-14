using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static ValidatedLinkRelationship ValidateLinkReferences(INavigation navigation, RepositoryCollectionReconciliationDesign collection)
    {
        if (collection.TargetReference is null && collection.AdditionalReferences.Count == 0)
            return new(ValidateLinkRelationship(navigation, collection.SharedReferenceColumnsEnabled), []);
        if (collection.TargetReference is null)
            throw new InvalidOperationException("RGD4201: Additional link references require an explicit TargetReference for complete identity matching.");

        var owner = navigation.ForeignKey;
        if (!owner.IsRequired || owner.IsUnique || owner.IsOwnership)
            throw new InvalidOperationException("RGD4201: Named link references require a required non-unique owner FK.");
        var target = Reference(collection.TargetReference, optional: false);
        var additional = collection.AdditionalReferences.Select(value => Reference(value.Navigation, value.Optional)).ToArray();
        var declared = new[] { owner, target }.Concat(additional).ToArray();
        if (declared.Distinct().Count() != declared.Length || navigation.TargetEntityType.GetForeignKeys().Any(key => !declared.Contains(key)))
            throw new InvalidOperationException("RGD4201: Declare every non-owner FK exactly once as TargetReference or AdditionalReference; roles cannot overlap or omit a relationship.");
        if (additional.Any(key => declared.Where(other => other != key).Any(other => other.Properties.Intersect(key.Properties).Any())))
            throw new InvalidOperationException("RGD4201: Additional references must have disjoint columns from ownership, target and each other; shared-column permission only applies to the matching target.");
        ValidateSharedLinkColumns(owner, target, collection.SharedReferenceColumnsEnabled);
        return new(target, additional);

        IForeignKey Reference(LambdaExpression selector, bool optional)
        {
            var body = selector.Body;
            while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion) body = conversion.Operand;
            if (body is not MemberExpression { Expression: ParameterExpression parameter } member || parameter != selector.Parameters[0] ||
                navigation.TargetEntityType.FindNavigation(member.Member.Name) is not { IsOnDependent: true, IsCollection: false } reference ||
                reference.ForeignKey == owner || !SupportedLinkReference(reference.ForeignKey, optional))
                throw new InvalidOperationException("RGD4201: Select an ordinary non-unique dependent reference with the declared required/optional cardinality, not ownership, a collection, owned/shared/inherited target or an unmapped member. Optional FKs require OptionalAdditionalReference; the matching target remains required.");
            if (optional && reference.ForeignKey.Properties.Any(static property => !property.IsNullable || property.ValueGenerated != ValueGenerated.Never ||
                property.ClrType.IsValueType && Nullable.GetUnderlyingType(property.ClrType) is null))
                throw new InvalidOperationException("RGD4301: Optional additional references require every FK component to be nullable in the model and CLR and not database-generated so an absent complete reference can be represented explicitly. Mixed-nullability/generated keys need an explicit adapter.");
            return reference.ForeignKey;
        }
    }

    private static bool SupportedLinkReference(IForeignKey reference, bool optional) => reference.IsRequired == !optional && !reference.IsUnique && !reference.IsOwnership &&
        !reference.PrincipalEntityType.IsOwned() && !reference.PrincipalEntityType.HasSharedClrType &&
        reference.PrincipalEntityType.BaseType is null && !reference.PrincipalEntityType.GetDerivedTypes().Any();

    private static ConfiguredLinkReconciliationModel ConfiguredLinkModel(INavigation navigation,
        ValidatedLinkRelationship references, RepositoryCollectionReconciliationDesign collection)
        => LinkModel(navigation, references.Target) with
        {
            TargetNavigationName = collection.TargetReference is null ? null : references.Target.DependentToPrincipal!.Name,
            AdditionalReferences = references.Additional.Count == 0 ? null : references.Additional
                .OrderBy(static key => key.DependentToPrincipal!.Name, StringComparer.Ordinal)
                .Select(static key => new ConfiguredAdditionalReferenceModel(key.DependentToPrincipal!.Name,
                    CSharpTypeName(key.PrincipalEntityType.ClrType), key.Properties.Select(static property => property.Name).ToArray(),
                    key.PrincipalKey.Properties.Select(static property => property.Name).ToArray(), key.IsRequired ? null : true)).ToArray()
        };
}
