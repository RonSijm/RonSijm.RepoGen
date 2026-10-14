using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryEntityNavigationMemberModel[]? CreateInMemoryMappedNavigationMembers(IEntityType entity,
        RepositoryEntityDesign definition, RepositoryGenerationSettingsModel settings, RepositoryDesign? design)
    {
        if (!settings.InMemoryTestQueries) return null;
        var members = new List<InMemoryEntityNavigationMemberModel>();
        foreach (var declaration in definition.InMemoryNavigations.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            var navigation = (INavigationBase?)entity.FindNavigation(declaration.Name) ?? entity.FindSkipNavigation(declaration.Name);
            if (navigation is null) continue; // Shadow declarations are checked separately.
            var target = navigation.TargetEntityType;
            // A configured target is not necessarily selected for fixture output. Never emit a
            // dangling slot type for an entity whose store/read-row helper cannot be generated.
            if (target.IsOwned() || target.HasSharedClrType || target.ClrType.IsNested || target.ClrType.IsGenericType) continue;
            var targetDefinition = design?.Entities.FirstOrDefault(item => item.EntityType == target.ClrType);
            if (targetDefinition is not { RepositoryEnabled: true, TestDoubleEnabled: not false, InMemoryStore: { } store } ||
                !targetDefinition.ReadEnabled && !targetDefinition.WriteEnabled) continue;
            var mapped = store.RowType != target.ClrType && store.EntityMapping is not null;
            if (!mapped && (store.RowType != target.ClrType || !target.ClrType.IsVisible)) continue;
            var memberName = navigation.PropertyInfo?.Name ?? navigation.FieldInfo?.Name;
            if (memberName is null) continue;
            if (memberName == "__RepoGen" + entity.ClrType.Name + "ReadRow" || memberName == "Finalize")
                throw new InvalidOperationException("RGD3203: Mapped navigation '" + entity.ClrType.FullName + "." + memberName + "' collides with a reserved read-row declaration.");
            members.Add(new(memberName, mapped ? "global::" + settings.ScriptedTestDoubleNamespace + ".__RepoGen" + target.ClrType.Name + "ReadRow" : CSharpTypeName(target.ClrType), navigation.IsCollection,
                navigation is INavigation { IsOnDependent: true, ForeignKey.IsRequired: true }));
        }
        return members.Count == 0 ? null : members.ToArray();
    }
}
