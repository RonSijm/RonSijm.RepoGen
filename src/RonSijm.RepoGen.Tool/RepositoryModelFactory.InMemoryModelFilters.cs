using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static (LambdaExpression Expression, InMemoryModelQueryFilterModel Model)? CreateInMemoryModelQueryFilter(
        IEntityType entity, RepositoryEntityDesign? definition, RepositoryQueryDesign query,
        SortedSet<string> reasons, InMemoryTranslationExpressions? translations)
        => query.IgnoreQueryFilters ? null : CreateInMemoryModelQueryFilter(entity,
            definition?.InMemoryModelQueryFilterEnabled == true, reasons, translations);

    private static (LambdaExpression Expression, InMemoryModelQueryFilterModel Model)? CreateInMemoryModelQueryFilter(
        IEntityType entity, bool enabled, SortedSet<string> reasons, InMemoryTranslationExpressions? translations)
    {
        if (!enabled || entity.GetQueryFilter() is not { } filter)
            return null;
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || filter.Parameters.Count != 1 ||
            filter.Parameters[0].Type != entity.ClrType || filter.ReturnType != typeof(bool))
            failures.Add("Only one ordinary row parameter and a Boolean result are supported, without inheritance/discriminators.");
        var expanded = translations?.Expand(filter) ?? filter;
        new InMemoryQueryExpressionInspector(failures).Visit(expanded);
        new InMemoryTranslationReplacementInspector(failures).Visit(expanded);
        if (expanded.Parameters.Count == 1)
            new InMemoryModelQueryFilterInspector(entity, expanded.Parameters[0], failures).Visit(expanded);
        string? source = null;
        if (failures.Count == 0)
        {
            try { source = ProjectionExpressionRenderer.Render(expanded); }
            catch (InvalidOperationException exception) { failures.Add(exception.Message); }
            catch (NotSupportedException exception) { failures.Add(exception.Message); }
        }
        foreach (var failure in failures)
            reasons.Add("Model query filter '" + entity.ClrType.FullName + "': " + failure);
        return source is null ? null : (expanded, new(CSharpTypeName(entity.ClrType), source));
    }
}
