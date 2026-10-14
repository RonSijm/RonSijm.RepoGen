using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

/// <summary>Proves local CLR null safety without evaluating designer expressions or allowing entity identity escape.</summary>
internal sealed class InMemoryNullableTargetExpressionInspector(ParameterExpression target, bool mayBeNull) : ExpressionVisitor
{
    private bool nonNull = !mayBeNull;
    internal bool Supported { get; private set; } = true;

    protected override Expression VisitParameter(ParameterExpression node)
    {
        // Whole entities cannot be hidden in DTOs/object casts/helpers and dereferenced later.
        if (node == target) Supported = false;
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is not null && StripConvert(node.Expression) == target)
        {
            if (!nonNull) Supported = false;
            return node;
        }
        return base.VisitMember(node);
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (NullTest(node, target) is not null) return node;
        if (node.Method is null && node.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
        {
            Visit(node.Left);
            VisitKnown(node.Right, NonNullWhen(node.Left, target, node.NodeType == ExpressionType.AndAlso));
            return node;
        }
        return base.VisitBinary(node);
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        Visit(node.Test);
        VisitKnown(node.IfTrue, NonNullWhen(node.Test, target, whenTrue: true));
        VisitKnown(node.IfFalse, NonNullWhen(node.Test, target, whenTrue: false));
        return node;
    }

    private void VisitKnown(Expression expression, bool known)
    {
        var previous = nonNull;
        nonNull |= known;
        Visit(expression);
        nonNull = previous;
    }

    internal static bool NonNullWhen(Expression expression, ParameterExpression target, bool whenTrue)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Not, Method: null } negated)
            return NonNullWhen(negated.Operand, target, !whenTrue);
        if (NullTest(expression, target) is { } isNonNull) return isNonNull == whenTrue;
        return expression is BinaryExpression { Method: null } logical &&
            (whenTrue && logical.NodeType == ExpressionType.AndAlso || !whenTrue && logical.NodeType == ExpressionType.OrElse) &&
            (NonNullWhen(logical.Left, target, whenTrue) || NonNullWhen(logical.Right, target, whenTrue));
    }

    private static bool? NullTest(Expression expression, ParameterExpression target)
    {
        if (expression is BinaryExpression { Method: null, NodeType: ExpressionType.Equal or ExpressionType.NotEqual } comparison &&
            (StripConvert(comparison.Left) == target && comparison.Right is ConstantExpression { Value: null } ||
             StripConvert(comparison.Right) == target && comparison.Left is ConstantExpression { Value: null }))
            return comparison.NodeType == ExpressionType.NotEqual;
        return null;
    }

    internal static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert, Method: null } conversion) expression = conversion.Operand;
        return expression;
    }
}
