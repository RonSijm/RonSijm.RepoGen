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
        foreach (var reference in additional)
        {
            var overlaps = declared.Where(other => other != reference && other.Properties.Intersect(reference.Properties).Any()).ToArray();
            if (overlaps.Length == 0) continue;
            if (overlaps.Any(other => (!reference.IsRequired || !other.IsRequired) &&
                reference.Properties.Intersect(other.Properties).Any(property => !owner.Properties.Contains(property))))
                throw new InvalidOperationException("RGD4201: Optional additional reference overlaps must be owner-shared columns; non-owner overlaps do not have an inferred absence policy.");
            if (!collection.SharedReferenceColumnsEnabled)
                throw new InvalidOperationException("RGD3902: Overlapping additional FK columns need explicit AllowSharedReferenceColumns; shared owner values must agree before binding.");
            ValidateSharedLinkColumns(owner, reference, allowSharedColumns: true);
            foreach (var property in reference.Properties.Where(property => overlaps.Any(other => other.Properties.Contains(property))))
                if (declared.Where(other => other.Properties.Contains(property)).Any(other =>
                    other.PrincipalKey.Properties[other.Properties.ToList().IndexOf(property)].ClrType != property.ClrType))
                    throw new InvalidOperationException("RGD3902: Shared additional reference columns require identical dependent/principal CLR types.");
        }
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
            if (optional)
            {
                if (reference.ForeignKey.Properties.Any(owner.Properties.Contains) && !collection.SharedReferenceColumnsEnabled)
                    throw new InvalidOperationException("RGD3902: Optional owner-shared reference columns require explicit AllowSharedReferenceColumns.");
                var independent = reference.ForeignKey.Properties.Where(property => !owner.Properties.Contains(property)).ToArray();
                if (independent.Length == 0 || reference.ForeignKey.Properties.Any(static property => property.ValueGenerated != ValueGenerated.Never) ||
                    independent.Any(static property => !property.IsNullable || property.ClrType.IsValueType && Nullable.GetUnderlyingType(property.ClrType) is null))
                    throw new InvalidOperationException("RGD4301: Optional additional references need independent, non-generated nullable components in the model and CLR. Only explicitly owner-shared components can remain non-nullable; mixed independent/generated keys need an adapter.");
            }
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
                .Select(key => new ConfiguredAdditionalReferenceModel(key.DependentToPrincipal!.Name,
                    CSharpTypeName(key.PrincipalEntityType.ClrType), key.Properties.Select(static property => property.Name).ToArray(),
                    key.PrincipalKey.Properties.Select(static property => property.Name).ToArray(), key.IsRequired ? null : true,
                    AdditionalSharedColumns(navigation.ForeignKey, references, key),
                    !key.IsRequired && key.Properties.Any(navigation.ForeignKey.Properties.Contains)
                        ? key.Properties.Where(property => !navigation.ForeignKey.Properties.Contains(property)).Select(static property => property.Name).ToArray() : null)).ToArray()
        };

    private static ConfiguredAdditionalSharedColumnModel[]? AdditionalSharedColumns(IForeignKey owner,
        ValidatedLinkRelationship references, IForeignKey reference)
    {
        var roles = new[] { (Key: owner, Name: "Owner"), (Key: references.Target, Name: "Target:" + references.Target.DependentToPrincipal!.Name) }
            .Concat(references.Additional.Where(key => key != reference).Select(static key => (Key: key, Name: "Additional:" + key.DependentToPrincipal!.Name))).ToArray();
        var columns = reference.Properties.Select(property => (Property: property,
                Roles: roles.Where(role => role.Key.Properties.Contains(property)).Select(static role => role.Name).Order(StringComparer.Ordinal).ToArray()))
            .Where(static column => column.Roles.Length != 0)
            .Select(column => new ConfiguredAdditionalSharedColumnModel(column.Property.Name, CSharpTypeName(column.Property.ClrType),
                owner.Properties.Contains(column.Property) ? owner.PrincipalKey.Properties[owner.Properties.ToList().IndexOf(column.Property)].Name : null,
                column.Roles)).ToArray();
        return columns.Length == 0 ? null : columns;
    }
}
