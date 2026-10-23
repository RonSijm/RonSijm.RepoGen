using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static (LambdaExpression Expression, InMemoryModelQueryFilterModel Model)? CreateInMemoryModelQueryFilter(
        IEntityType entity, RepositoryEntityDesign? definition, RepositoryQueryDesign query,
        SortedSet<string> reasons, InMemoryTranslationExpressions? translations, RepositoryDesign? design)
        => query.IgnoreQueryFilters ? null : CreateInMemoryModelQueryFilter(entity,
            definition?.InMemoryModelQueryFilterEnabled == true, reasons, translations, design);

    private static (LambdaExpression Expression, InMemoryModelQueryFilterModel Model)? CreateInMemoryModelQueryFilter(
        IEntityType entity, bool enabled, SortedSet<string> reasons, InMemoryTranslationExpressions? translations, RepositoryDesign? design)
    {
        if (!enabled || entity.GetQueryFilter() is not { } filter)
            return null;
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || filter.Parameters.Count != 1 ||
            filter.Parameters[0].Type != entity.ClrType || filter.ReturnType != typeof(bool))
            failures.Add("Only one ordinary row parameter and a Boolean result are supported, without inheritance/discriminators.");
        var binding = design?.Options.ContextKeyType is { } keyType && design.InMemoryModelFilterContexts.Count != 0
            ? new InMemoryModelFilterContextExpressions(design.InMemoryModelFilterContexts,
                Expression.Parameter(keyType, InMemoryModelFilterContextExpressions.KeyVariable)) : null;
        var bound = binding?.Bind(filter) ?? filter;
        var expanded = translations?.Expand(bound) ?? bound;
        new InMemoryQueryExpressionInspector(failures).Visit(expanded);
        new InMemoryTranslationReplacementInspector(failures).Visit(expanded);
        if (expanded.Parameters.Count >= 1)
            new InMemoryModelQueryFilterInspector(entity, expanded.Parameters[0], failures).Visit(expanded);
        string? source = null;
        if (failures.Count == 0)
        {
            try
            {
                source = expanded.Parameters.Count == 1 ? ProjectionExpressionRenderer.Render(expanded)
                    : "entity => " + ProjectionExpressionRenderer.RenderBody(expanded, ["entity", InMemoryModelFilterContextExpressions.KeyVariable]);
            }
            catch (InvalidOperationException exception) { failures.Add(exception.Message); }
            catch (NotSupportedException exception) { failures.Add(exception.Message); }
        }
        foreach (var failure in failures)
            reasons.Add("Model query filter '" + entity.ClrType.FullName + "': " + failure);
        return source is null ? null : (expanded, new(CSharpTypeName(entity.ClrType), source)
            { ContextBindings = binding?.Receipts.Count > 0 ? binding.Receipts : null });
    }
}
