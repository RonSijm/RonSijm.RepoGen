using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Requires local null guards for optional references whose targets can disappear through a filter.</summary>
internal sealed class InMemoryFilteredReferenceUsageInspector(INavigation navigation) : ExpressionVisitor
{
    private readonly List<MemberExpression> references = [];

    internal bool Check(Expression expression)
    {
        references.Clear();
        Visit(expression);
        foreach (var reference in references)
        {
            var target = Expression.Parameter(reference.Type, "__filteredReference");
            var replaced = new ReferenceReplacer(reference, target).Visit(expression)!;
            var safety = new InMemoryNullableTargetExpressionInspector(target, mayBeNull: true);
            safety.Visit(replaced);
            if (!safety.Supported) return false;
        }
        return true;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Member == navigation.PropertyInfo && node.Expression is not null &&
            navigation.DeclaringEntityType.Model.FindEntityType(node.Expression.Type) == navigation.DeclaringEntityType &&
            !references.Any(reference => SameReference(reference, node)))
            references.Add(node);
        return base.VisitMember(node);
    }

    // Only ordinary parameter/member receivers establish the same value. Do not infer equality
    // between calls, indices, captured state or separately bound lambda parameters.
    private static bool SameReference(Expression left, Expression right)
    {
        left = InMemoryNullableTargetExpressionInspector.StripConvert(left);
        right = InMemoryNullableTargetExpressionInspector.StripConvert(right);
        if (left == right) return true;
        return left is MemberExpression a && right is MemberExpression b && a.Member == b.Member &&
            a.Expression is not null && b.Expression is not null && SameReference(a.Expression, b.Expression);
    }

    private sealed class ReferenceReplacer(MemberExpression reference, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node) => SameReference(reference, node) ? target : base.VisitMember(node);
    }
}
