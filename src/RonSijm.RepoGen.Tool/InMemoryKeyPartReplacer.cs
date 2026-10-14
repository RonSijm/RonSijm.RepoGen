using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

internal sealed class InMemoryKeyPartReplacer(IReadOnlyDictionary<Expression, Expression> replacements) : ExpressionVisitor
{
    public override Expression? Visit(Expression? node) => node is not null && replacements.TryGetValue(node, out var value) ? value : base.Visit(node);
}
