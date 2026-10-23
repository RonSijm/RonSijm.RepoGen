using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Accepts only uses that discard an optional target before observing the related sequence.</summary>
/// <remarks>EF may retain null targets in a skip-navigation projection. The detached loader cannot
/// emulate SQL null propagation; a leading reference-null guard makes its non-null rows explicit.</remarks>
internal sealed class InMemoryOptionalJoinUsageInspector(ISkipNavigation navigation) : ExpressionVisitor
{
    internal bool Supported { get; private set; } = true;

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(Enumerable) && node.Method.Name == nameof(Enumerable.Where) &&
            node.Arguments.Count == 2 && StripConvert(node.Arguments[0]) is MemberExpression member && Matches(member) &&
            StripQuote(node.Arguments[1]) is LambdaExpression { Parameters.Count: 1 } predicate &&
            HasLeadingNullGuard(predicate.Body, predicate.Parameters[0]))
        {
            // This occurrence, not the MemberExpression globally, is guarded. An expression DAG may
            // reuse the same member in a separate unguarded count/projection in the same operation.
            Visit(member.Expression);
            Visit(predicate);
            return node;
        }
        return base.VisitMethodCall(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (Matches(node)) Supported = false;
        return base.VisitMember(node);
    }

    private bool Matches(MemberExpression member) => member.Member.Name == navigation.Name &&
        member.Expression is not null && navigation.DeclaringEntityType.Model.FindEntityType(member.Expression.Type) == navigation.DeclaringEntityType;

    private static bool HasLeadingNullGuard(Expression body, ParameterExpression target) => body switch
    {
        BinaryExpression { NodeType: ExpressionType.AndAlso, Method: null } conjunction => HasLeadingNullGuard(conjunction.Left, target),
        BinaryExpression { NodeType: ExpressionType.NotEqual, Method: null } comparison =>
            StripConvert(comparison.Left) == target && comparison.Right is ConstantExpression { Value: null } ||
            StripConvert(comparison.Right) == target && comparison.Left is ConstantExpression { Value: null },
        _ => false
    };

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert, Method: null } conversion)
            expression = conversion.Operand;
        return expression;
    }

    private static Expression StripQuote(Expression expression) => expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;
}
