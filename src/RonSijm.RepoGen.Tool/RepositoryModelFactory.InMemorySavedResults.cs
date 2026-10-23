using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemorySavedResultModel? CreateInMemorySavedResult(IEntityType entity, RepositoryEntityDesign definition,
        RepositoryCommandDesign command, RepositoryGenerationSettingsModel settings, IModel? metadata,
        RepositoryDesign? design, InMemoryEntityMappingModel? parentMapping, SortedSet<string> reasons, InMemoryTranslationExpressions? translations = null)
    {
        if (!command.ResultFromSaved) return null;
        var projection = command.ResultProjection!;
        var expressions = new[] { (Expression)projection }.Concat(definition.VisibilityPolicies
            .Where(policy => !command.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal))
            .Select(static policy => (Expression)policy.Predicate)).Select(expression => translations?.Visit(expression) ?? expression).ToArray();
        var hasNavigation = expressions.Any(expression => RepositoryQuerySourceRenderer.NavigationReferences(entity.Model, expression).Count != 0);
        if (hasNavigation && !settings.InMemoryTestQueries)
        {
            reasons.Add("ReturnsSaved navigation projections require UseInMemoryTestQueries and explicit HasInMemoryNavigation declarations.");
            return null;
        }
        var mappings = new List<(IEntityType Entity, InMemoryEntityMappingModel Mapping)>();
        var modelFilters = new List<(LambdaExpression Expression, InMemoryModelQueryFilterModel Model)>();
        if (parentMapping is not null) mappings.Add((entity, parentMapping));
        var navigation = CreateInMemoryNavigations(entity, null, design, [], expressions, null, reasons, metadata, settings, mappings,
            modelFilters, translations, filterPermission: command.InMemoryModelQueryFilterNavigationsEnabled
                ? (target, relatedDefinition) => target == entity || relatedDefinition?.InMemoryModelQueryFilterNavigationsEnabled == true
                : null);
        expressions = [.. expressions, .. modelFilters.Select(static filter => (Expression)filter.Expression)];
        foreach (var expression in expressions) new InMemoryQueryExpressionInspector(reasons).Visit(expression);
        foreach (var (mappedEntity, mapping) in mappings.DistinctBy(static item => item.Entity))
        {
            bool ContainsModel(Type type) => type == mappedEntity.ClrType || type.HasElementType && ContainsModel(type.GetElementType()!) ||
                type.IsGenericType && type.GetGenericArguments().Any(ContainsModel);
            if (ContainsModel(projection.ReturnType))
                reasons.Add("ReturnsSaved mapped sources cannot return persistence entity identity; project scalar/DTO values instead.");
            var inspector = new InMemoryEntityMappingExpressionInspector(mappedEntity, mapping, reasons);
            foreach (var expression in expressions) inspector.Visit(expression);
        }
        return new(navigation, modelFilters.Count == 0 ? null : modelFilters.Select(static filter => filter.Model).ToArray());
    }
}
