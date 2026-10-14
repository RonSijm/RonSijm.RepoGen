using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static string? CreateInMemoryScalarCreate(IEntityType entity, RepositoryEntityDesign definition, RepositoryCommandDesign command,
        string rowTypeName, List<FilterParameterModel> parameters, SortedSet<string> reasons, InMemoryTranslationExpressions? translations = null, IModel? metadata = null)
    {
        var mapping = command.CreateMany?.Mapping ?? command.CreateMapping;
        if (mapping?.Body is not MemberInitExpression { NewExpression.Arguments.Count: 0 } initializer) return null;
        var assigned = initializer.Bindings.OfType<MemberAssignment>().ToDictionary(binding => binding.Member.Name);
        var values = new List<string>();
        foreach (var property in entity.GetProperties().OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (assigned.TryGetValue(property.Name, out var assignment))
            {
                var expression = Expression.Lambda(translations?.Visit(assignment.Expression) ?? assignment.Expression, mapping.Parameters);
                var value = command.CreateMany is null ? RenderBoundExpression(expression, parameters, skipRow: false, lambda: false)
                    : ProjectionExpressionRenderer.RenderBody(expression, ["item"]);
                // Expression trees erase null-forgiving syntax. A generated scalar may intentionally
                // start at null before its explicit insert factory supplies the final value.
                if (!property.IsNullable && !property.ClrType.IsValueType && assignment.Expression is ConstantExpression { Value: null }) value += "!";
                values.Add(EscapeIdentifier(property.Name) + " = " + value);
            }
            else
            {
                if ((property.ValueGenerated & ValueGenerated.OnAdd) == 0 ||
                    !HasInMemoryValueAdapter(property, definition.InMemoryStore, metadata))
                    reasons.Add("Scalar fixture insertion must explicitly assign scalar '" + property.Name + "' or declare its generated-value factory; persistence constructor/database default behavior is not simulated.");
                if (property.GetBeforeSaveBehavior() != PropertySaveBehavior.Ignore && !Equals(property.Sentinel, property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null))
                    reasons.Add("Scalar fixture insertion must explicitly assign custom-sentinel property '" + property.Name + "'; persistence constructor/field initializer behavior is not inferred.");
                values.Add(EscapeIdentifier(property.Name) + " = default(" + CSharpTypeName(property.ClrType) + ")!");
            }
        }
        return "new " + rowTypeName + " { " + string.Join(", ", values) + " }";
    }
}
