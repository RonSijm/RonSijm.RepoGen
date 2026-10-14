using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static IForeignKey ValidateLinkRelationship(INavigation navigation, bool allowSharedColumns)
    {
        var owner = navigation.ForeignKey;
        var foreignKeys = navigation.TargetEntityType.GetForeignKeys().ToArray();
        if (foreignKeys.Length != 2 || !owner.IsRequired || owner.IsUnique || owner.IsOwnership ||
            foreignKeys.SingleOrDefault(key => key != owner) is not { IsRequired: true, IsUnique: false, IsOwnership: false } target ||
            target.PrincipalEntityType.IsOwned() || target.PrincipalEntityType.HasSharedClrType ||
            target.PrincipalEntityType.BaseType is not null || target.PrincipalEntityType.GetDerivedTypes().Any())
            throw new InvalidOperationException(allowSharedColumns
                ? "RGD3901: ReconcileLinks requires an ordinary explicit leaf join entity with exactly two required many-to-one foreign keys: selected owner and existing target. Shared columns do not permit optional/shared-property-bag/owned/inherited/third-FK relationships."
                : "RGD3901: ReconcileLinks requires an ordinary explicit leaf join entity with exactly two required, disjoint many-to-one foreign keys: selected owner and existing target. Optional/shared/owned/inherited/overlapping/third-FK relationships need an explicit adapter.");
        ValidateSharedLinkColumns(owner, target, allowSharedColumns);
        return target;
    }

    private static void ValidateSharedLinkColumns(IForeignKey owner, IForeignKey target, bool allowSharedColumns)
    {
        var shared = owner.Properties.Intersect(target.Properties).ToArray();
        if (shared.Length != 0 && !allowSharedColumns)
            throw new InvalidOperationException("RGD3902: Overlapping owner/target FK columns require explicit AllowSharedReferenceColumns; reference keys must agree with the selected owner.");
        if (shared.Length == target.Properties.Count || shared.Any(property => property.ClrType !=
                owner.PrincipalKey.Properties[owner.Properties.ToList().IndexOf(property)].ClrType))
            throw new InvalidOperationException("RGD3902: Shared reference columns require identical owner/key CLR types and at least one independent target FK component.");
    }

    private static ConfiguredLinkReconciliationModel LinkModel(INavigation navigation, IForeignKey target)
        => new(CSharpTypeName(target.PrincipalEntityType.ClrType),
            navigation.ForeignKey.Properties.Select(static property => property.Name).ToArray(),
            target.Properties.Select(static property => property.Name).ToArray(),
            target.PrincipalKey.Properties.Select(static property => property.Name).ToArray(), SharedReferenceColumns(navigation.ForeignKey, target));

    private static ConfiguredSharedReferenceColumnModel[]? SharedReferenceColumns(IForeignKey owner, IForeignKey target)
    {
        var columns = target.Properties.Select((property, index) => (property, index))
            .Where(pair => owner.Properties.Contains(pair.property))
            .Select(pair => new ConfiguredSharedReferenceColumnModel(pair.property.Name,
                owner.PrincipalKey.Properties[owner.Properties.ToList().IndexOf(pair.property)].Name,
                CSharpTypeName(pair.property.ClrType), pair.index)).ToArray();
        return columns.Length == 0 ? null : columns;
    }
}
