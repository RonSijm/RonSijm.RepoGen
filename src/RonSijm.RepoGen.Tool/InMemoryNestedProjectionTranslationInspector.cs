using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Tool;

/// <summary>Explicit fresh data construction only; no entity/service graph or deferred result escape.</summary>
internal sealed class InMemoryNestedProjectionTranslationInspector(ISet<string> reasons, HashSet<Type> allowedDtos) : ExpressionVisitor
{
    protected override Expression VisitNew(NewExpression node)
    {
        if (InMemoryTranslationExpressions.Scalar(node.Type)) return base.VisitNew(node);
        if (node.Constructor?.IsPublic == false || !Value(node.Type))
            reasons.Add("Nested projection replacements may construct only approved independent public DTOs and standard lists through public constructors.");
        else if (!InMemoryProjectionDataShape.List(node.Type) && node.Constructor is { } constructor)
        {
            foreach (var argument in node.Arguments.Zip(constructor.GetParameters()))
                if (!Value(argument.Second.ParameterType) || !Constructed(argument.First, argument.Second.ParameterType))
                    reasons.Add("Nested DTO constructor arguments must be scalar values or explicitly constructed DTO/materialized collection data.");
        }
        return base.VisitNew(node);
    }

    protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
    {
        var type = node.Member switch
        {
            PropertyInfo property when property.SetMethod?.IsPublic == true && !property.SetMethod.IsStatic => property.PropertyType,
            FieldInfo field when field.IsPublic && !field.IsStatic && !field.IsInitOnly => field.FieldType,
            _ => null
        };
        if (type is null || !Value(type) || !Constructed(node.Expression, type))
            reasons.Add("Nested DTO initializers require public writable data members and explicit DTO/materialized collection construction.");
        return base.VisitMemberAssignment(node);
    }

    protected override Expression VisitNewArray(NewArrayExpression node)
    {
        if (node.NodeType != ExpressionType.NewArrayInit || !Value(node.Type) ||
            node.Expressions.Any(expression => !Constructed(expression, node.Type.GetElementType()!)))
            reasons.Add("Nested result arrays require explicit approved scalar/DTO elements, not array bounds, entities or arbitrary objects.");
        return base.VisitNewArray(node);
    }

    protected override Expression VisitListInit(ListInitExpression node)
    {
        if (!InMemoryProjectionDataShape.List(node.Type) || !Value(node.Type) ||
            node.Initializers.Any(initializer => initializer.AddMethod.DeclaringType != node.Type || initializer.AddMethod.Name != "Add" ||
                initializer.Arguments.Count != 1 || !Constructed(initializer.Arguments[0], node.Type.GetGenericArguments()[0])))
            reasons.Add("Nested result collection initializers support only standard List<T> with approved scalar/DTO elements.");
        return base.VisitListInit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var orderedData = node.Method.DeclaringType == typeof(Enumerable) && node.Type.IsGenericType &&
            node.Type.GetGenericTypeDefinition() == typeof(IOrderedEnumerable<>) && Value(node.Type.GetGenericArguments()[0]);
        if (!InMemoryTranslationExpressions.Scalar(node.Type) && !Value(node.Type) && !orderedData)
            reasons.Add("Nested replacement helpers may not introduce entity, service or unsupported collection results.");
        return base.VisitMethodCall(node);
    }

    protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding node)
    {
        reasons.Add("Assign a freshly constructed nested DTO explicitly; implicit nested member mutation is not result construction.");
        return base.VisitMemberMemberBinding(node);
    }

    protected override MemberListBinding VisitMemberListBinding(MemberListBinding node)
    {
        reasons.Add("Assign a freshly materialized array/list explicitly; implicit collection member mutation is not result construction.");
        return base.VisitMemberListBinding(node);
    }

    private bool Value(Type type) => InMemoryProjectionDataShape.ValueType(type, allowedDtos);

    private bool Constructed(Expression expression, Type expected)
    {
        if (InMemoryTranslationExpressions.Scalar(expected)) return true;
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked, Method: null } convert) expression = convert.Operand;
        if (expression is ConstantExpression { Value: null }) return !expected.IsValueType || Nullable.GetUnderlyingType(expected) is not null;
        if (expression is ConditionalExpression conditional) return Constructed(conditional.IfTrue, expected) && Constructed(conditional.IfFalse, expected);
        if (InMemoryProjectionDataShape.CollectionElement(expected) is not { } element)
            return Value(expected) && InMemoryProjectionTranslationInspector.ConstructsResult(expression, expected);
        if (InMemoryProjectionDataShape.CollectionElement(expression.Type) != element) return false;
        return expression is NewArrayExpression { NodeType: ExpressionType.NewArrayInit } or ListInitExpression ||
            expression is NewExpression creation && InMemoryProjectionDataShape.List(creation.Type) ||
            expression is MethodCallExpression call &&
                (call.Method.DeclaringType == typeof(Enumerable) && call.Method.Name is nameof(Enumerable.ToArray) or nameof(Enumerable.ToList) ||
                 call.Method.DeclaringType == typeof(Array) && call.Method.Name == nameof(Array.Empty));
    }
}
