using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    internal static IReadOnlyList<InMemoryPropertyModel> CreateInMemoryProperties(IEntityType entity, RepositoryInMemoryStoreDesign store)
    {
        if (store.PropertyMappings.Count == 0)
        {
            return [];
        }
        InvalidOperationException Invalid(string reason) => new("RGD4901: Invalid MapInMemoryProperty for '" + entity.ClrType.FullName + "': " + reason);
        if (store.RowType == entity.ClrType || store.EntityMapping?.Body is not MemberInitExpression initializer)
        {
            throw Invalid("Use an explicit public data-shape store and MapInMemoryEntity scalar initializer.");
        }
        var names = initializer.Bindings.Select(static binding => binding.Member.Name).Append("__RepoGen" + entity.ClrType.Name + "ReadRow")
            .Concat(entity.GetProperties().SelectMany(static property => new[] { property.Name, property.PropertyInfo?.Name, property.FieldInfo?.Name }).OfType<string>())
            .Concat(entity.GetNavigations().Select(static navigation => navigation.Name)).Concat(entity.GetSkipNavigations().Select(static navigation => navigation.Name))
            .ToHashSet(StringComparer.Ordinal);
        var bindings = new List<InMemoryPropertyModel>();
        foreach (var declaration in store.PropertyMappings.OrderBy(static property => property.PropertyName, StringComparer.Ordinal))
        {
            var property = entity.FindProperty(declaration.PropertyName);
            if (property is null || declaration.Value.Parameters.Count != 1 || declaration.Value.Parameters[0].Type != store.RowType ||
                declaration.Value.ReturnType != property.ClrType || !InMemoryTranslationExpressions.Scalar(property.ClrType) && property.ClrType != typeof(byte[]))
            {
                throw Invalid("'" + declaration.PropertyName + "' must name a mapped scalar with the exact public CLR value type (or byte array), not a navigation or unmapped property.");
            }
            if (initializer.Bindings.Any(binding => binding.Member == property.PropertyInfo || binding.Member == property.FieldInfo))
            {
                throw Invalid("'" + declaration.PropertyName + "' is assigned by both MapInMemoryEntity and MapInMemoryProperty. Declare exactly one scalar source; overrides are not inferred.");
            }
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            new InMemoryQueryExpressionInspector(reasons).Visit(declaration.Value);
            new InMemoryMappingValueExpressionInspector(entity.Model, reasons).Visit(declaration.Value);
            new InMemoryTranslationReplacementInspector(reasons).Visit(declaration.Value);
            if (reasons.Count != 0)
            {
                throw Invalid(string.Join(" ", reasons));
            }
            string value;
            try
            {
                value = ProjectionExpressionRenderer.RenderBody(declaration.Value, ["row"]);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                throw Invalid(exception.Message);
            }
            var member = "__repoGenProperty" + bindings.Count;
            while (!names.Add(member))
            {
                member += "_";
            }
            var kind = property.IsShadowProperty() ? "Shadow" : property.IsIndexerProperty() ? "Indexer" :
                property.PropertyInfo is null ? "FieldOnly" : property.GetMemberInfo(forMaterialization: false, forSet: false) is FieldInfo ? "FieldBacked" : "ClrProperty";
            bindings.Add(new(property.Name, member, CSharpTypeName(property.ClrType, property.IsNullable), value, kind));
        }
        return bindings;
    }
}
