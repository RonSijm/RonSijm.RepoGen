using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Checks scalar coverage and prevents plain read rows from pretending to have entity identity/behavior.</summary>
internal sealed class InMemoryEntityMappingExpressionInspector(IEntityType entity, InMemoryEntityMappingModel mapping, ISet<string> reasons) : ExpressionVisitor
{
    private int allowReceiver;

    private void Identity() => reasons.Add("Mapped source '" + entity.ClrType.FullName +
        "' uses whole-entity identity, behavior or an explicit model type. Only mapped scalar reads are portable; use a typed script.");

    private bool ContainsEntity(Type type) => type == entity.ClrType ||
        type.HasElementType && ContainsEntity(type.GetElementType()!) ||
        type.IsGenericType && type.GetGenericArguments().Any(ContainsEntity);

    private void VisitValue(Expression? expression, bool receiver = false)
    {
        var previous = allowReceiver;
        allowReceiver = receiver ? 1 : 0;
        try { Visit(expression); }
        finally { allowReceiver = previous; }
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        VisitValue(node.Body); // Parameters are declarations, not whole-entity reads.
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node.Type == entity.ClrType && allowReceiver == 0) Identity();
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression?.Type == entity.ClrType)
        {
            var property = entity.GetProperties().SingleOrDefault(property => property.PropertyInfo == node.Member || property.FieldInfo == node.Member);
            var navigation = mapping.Navigations?.Any(member => member.Name == node.Member.Name) == true;
            if (!navigation && (property is null || !mapping.Members.Any(member => member.Name == node.Member.Name)))
                reasons.Add("Mapped source '" + entity.ClrType.FullName + "' does not map scalar member '" + node.Member.Name +
                    "'. Add an explicit assignment to MapInMemoryEntity or use a typed script.");
            VisitValue(node.Expression, receiver: true);
        }
        else
        {
            if (node.Type == entity.ClrType && allowReceiver == 0) Identity();
            VisitValue(node.Expression);
        }
        return node;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        var carrier = ProjectionExpressionRenderer.IsAnonymousType(node.Type);
        if (!carrier && ContainsEntity(node.Type)) Identity();
        foreach (var argument in node.Arguments)
            VisitValue(argument, receiver: carrier && ContainsEntity(argument.Type));
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == entity.ClrType || node.Object is not null && ContainsEntity(node.Object.Type) ||
            node.Type == entity.ClrType && allowReceiver == 0 ||
            node.Method.IsGenericMethod && node.Method.GetGenericArguments().Any(ContainsEntity) &&
            (!ProjectionExpressionRenderer.CanInferLinqArguments(node) || node.Method.Name is "Distinct" or "Union" or "Except" or "Intersect" or "Contains" or "SequenceEqual"))
            Identity();
        VisitValue(node.Object);
        foreach (var argument in node.Arguments) VisitValue(argument);
        return node;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        var nullCheck = node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual &&
            (node.Left is ConstantExpression { Value: null } || node.Right is ConstantExpression { Value: null });
        VisitValue(node.Left, receiver: nullCheck);
        VisitValue(node.Right, receiver: nullCheck);
        if (node.Conversion is not null) VisitValue(node.Conversion);
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType != ExpressionType.Quote && (ContainsEntity(node.Type) || ContainsEntity(node.Operand.Type))) Identity();
        VisitValue(node.Operand);
        return node;
    }

    protected override Expression VisitTypeBinary(TypeBinaryExpression node)
    {
        if (ContainsEntity(node.TypeOperand) || ContainsEntity(node.Expression.Type)) Identity();
        VisitValue(node.Expression);
        return node;
    }

    protected override Expression VisitNewArray(NewArrayExpression node)
    {
        if (ContainsEntity(node.Type)) Identity();
        return base.VisitNewArray(node);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value is Type type && ContainsEntity(type)) Identity();
        return node;
    }
}
