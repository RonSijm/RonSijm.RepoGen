using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryRelationshipMetadata
{
    internal static bool CanDeclareScalarEquality(IProperty property, IModel? designTimeModel)
    {
        var definition = designTimeModel?.FindEntityType(property.DeclaringType.Name)?.FindProperty(property.Name);
        return definition is IConventionProperty configured && property.GetValueConverter() is null && definition.GetValueConverter() is null &&
            configured.GetProviderValueComparerConfigurationSource() is null &&
            property.GetValueComparer()?.EqualsExpression.ToString() == definition.GetValueComparer()?.EqualsExpression.ToString() &&
            property.GetKeyValueComparer().EqualsExpression.ToString() == ((IProperty)definition).GetKeyValueComparer().EqualsExpression.ToString();
    }

    internal static bool RequiresCustomEquality(IProperty property, IModel? designTimeModel)
    {
        // Read-optimized models omit configuration annotations and wrap ordinary nullable comparers.
        // Use public design-time configuration-source APIs, not internal comparer implementation names.
        var definition = designTimeModel?.FindEntityType(property.DeclaringType.Name)?.FindProperty(property.Name);
        if (definition is not IConventionProperty configured) return true;
        if (property.GetValueConverter() is not null || definition.GetValueConverter() is not null ||
            configured.GetValueComparerConfigurationSource() is not null || configured.GetProviderValueComparerConfigurationSource() is not null)
            return true;
        // An externally supplied/compiled model must not silently disagree with the context's design model.
        // Comparing expression descriptions is only a conservative mismatch check; no comparer is invoked.
        return property.GetValueComparer()?.EqualsExpression.ToString() != definition.GetValueComparer()?.EqualsExpression.ToString() ||
            property.GetKeyValueComparer().EqualsExpression.ToString() != ((IProperty)definition).GetKeyValueComparer().EqualsExpression.ToString();
    }
}
