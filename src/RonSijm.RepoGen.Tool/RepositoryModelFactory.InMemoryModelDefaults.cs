using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static string? InMemoryModelDefault(IProperty property, RepositoryInMemoryStoreDesign store, IModel? metadata)
    {
        var configured = metadata?.FindEntityType(property.DeclaringType.Name)?.FindProperty(property.Name);
        var mapped = store.EntityMapping is not null && store.WriteMapping is not null;
        if (!store.ModelDefaultsEnabled || property.ValueGenerated != ValueGenerated.OnAdd || property.IsKey() ||
            property.IsForeignKey() || property.IsConcurrencyToken || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save ||
            configured is null || configured.GetDefaultValueSql() is not null || configured.GetComputedColumnSql() is not null ||
            configured.FindAnnotation(RelationalAnnotationNames.DefaultValue)?.Value is not { } value ||
            InMemorySentinelHasCustomComparison(property, metadata) ||
            !mapped && (property.PropertyInfo?.GetMethod?.IsPublic != true || property.PropertyInfo.SetMethod?.IsPublic != true ||
            property.PropertyInfo.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))))
            return null;
        return InMemoryScalarConstantRenderer.Render(value, property.ClrType);
    }

    private static bool HasInMemoryValueAdapter(IProperty property, RepositoryInMemoryStoreDesign? store, IModel? metadata) => store is not null &&
        (store.GeneratedValues.Any(adapter => adapter.PropertyName == property.Name) || InMemoryModelDefault(property, store, metadata) is not null);

    private static bool InMemorySentinelHasCustomComparison(IProperty property, IModel? metadata) =>
        InMemoryRelationshipMetadata.RequiresCustomEquality(property, metadata) ||
        metadata?.FindEntityType(property.DeclaringType.Name)?.FindProperty(property.Name)?.GetProviderClrType() is not null;
}
