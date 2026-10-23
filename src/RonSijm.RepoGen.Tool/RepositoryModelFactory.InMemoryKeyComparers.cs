using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static string? CreateInMemoryKeyComparer(IEntityType entity, RepositoryInMemoryStoreDesign store)
    {
        if (store.KeyComparer is not { } comparer) return null;
        InvalidOperationException Invalid(string reason) => new("RGD3204: Invalid fixture key comparer for '" + entity.ClrType.FullName + "': " + reason);
        return CreateInMemoryComparerFactory(entity.Model, store.KeyType, comparer, Invalid);
    }

    private static string CreateInMemoryComparerFactory(IModel model, Type keyType, LambdaExpression comparer,
        Func<string, InvalidOperationException> invalid)
    {
        if (comparer.Parameters.Count != 0 || comparer.ReturnType != typeof(IEqualityComparer<>).MakeGenericType(keyType))
            throw invalid("Use a parameterless factory returning IEqualityComparer<TKey> for the exact declared key component type.");
        var body = comparer.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked, Method: null } cast) body = cast.Operand;
        if (body is not (MemberExpression or NewExpression)) throw invalid("Construct a public comparer or select an allowed default; null and captured comparer values are not allowed.");
        if (!comparer.ReturnType.IsAssignableFrom(body.Type)) throw invalid("The constructed/selected comparer must implement IEqualityComparer<TKey> for the exact key type, without an object-cast escape hatch.");
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        new InMemoryKeyComparerExpressionInspector(reasons).Visit(comparer.Body);
        new InMemoryMappingValueExpressionInspector(model, reasons).Visit(comparer.Body);
        if (reasons.Count != 0) throw invalid(string.Join(" ", reasons));
        try { return ProjectionExpressionRenderer.RenderBody(comparer, []); }
        catch (InvalidOperationException exception) { throw invalid(exception.Message); }
    }
}
