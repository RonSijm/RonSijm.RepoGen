using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Explicit flat public data construction, not an entity/service/graph translation escape hatch.</summary>
internal sealed class InMemoryProjectionTranslationInspector(ISet<string> reasons, HashSet<Type> allowedDtos) : ExpressionVisitor
{
    internal static bool FlatDto(Type type, IModel? model)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!DtoCandidate(type, model)) return false;
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        return properties.Length + fields.Length > 0 &&
            properties.All(property => property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0 && InMemoryTranslationExpressions.Scalar(property.PropertyType)) &&
            fields.All(field => InMemoryTranslationExpressions.Scalar(field.FieldType));
    }

    internal static bool DtoCandidate(Type type, IModel? model) =>
        !(model is null || !type.IsVisible || type.IsAbstract || type.IsInterface || type.IsArray || type.ContainsGenericParameters ||
            type == typeof(object) || InMemoryTranslationExpressions.Scalar(type) || typeof(IEnumerable).IsAssignableFrom(type) ||
            typeof(IDisposable).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type) ||
            type.Namespace is { } ns && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal)) ||
            model.GetEntityTypes().Any(entity => Contains(type, entity.ClrType)));

    private static bool Contains(Type type, Type entity) => entity.IsAssignableFrom(type) || type.HasElementType && Contains(type.GetElementType()!, entity) ||
        type.IsGenericType && type.GetGenericArguments().Any(argument => Contains(argument, entity));

    internal static bool ConstructsResult(Expression expression, Type resultType)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked, Method: null } conversion) expression = conversion.Operand;
        var type = Nullable.GetUnderlyingType(resultType) ?? resultType;
        return expression switch
        {
            NewExpression creation => creation.Type == type,
            MemberInitExpression initializer => initializer.Type == type,
            ConditionalExpression conditional => ConstructsResult(conditional.IfTrue, resultType) && ConstructsResult(conditional.IfFalse, resultType),
            ConstantExpression { Value: null } => !resultType.IsValueType || Nullable.GetUnderlyingType(resultType) is not null,
            _ => false
        };
    }

    protected override Expression VisitNew(NewExpression node)
    {
        if (!InMemoryTranslationExpressions.Scalar(node.Type) && (!allowedDtos.Contains(node.Type) || node.Constructor?.IsPublic == false ||
            node.Constructor?.GetParameters().Any(parameter => !InMemoryTranslationExpressions.Scalar(parameter.ParameterType)) == true))
            reasons.Add("Projection replacements may construct only explicitly registered public flat DTOs with public scalar constructors, not entities, services or arbitrary graphs.");
        return base.VisitNew(node);
    }

    protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
    {
        if (node.Member is PropertyInfo property && (property.SetMethod?.IsPublic != true || property.SetMethod.IsStatic || !InMemoryTranslationExpressions.Scalar(property.PropertyType)) ||
            node.Member is FieldInfo field && (!field.IsPublic || field.IsStatic || field.IsInitOnly || !InMemoryTranslationExpressions.Scalar(field.FieldType)))
            reasons.Add("DTO initializers require public writable scalar members.");
        return base.VisitMemberAssignment(node);
    }

    protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding node)
    {
        reasons.Add("Nested DTO member binding is graph construction, not a flat projection replacement.");
        return base.VisitMemberMemberBinding(node);
    }

    protected override MemberListBinding VisitMemberListBinding(MemberListBinding node)
    {
        reasons.Add("Collection DTO member binding is outside flat projection replacement semantics.");
        return base.VisitMemberListBinding(node);
    }
}
