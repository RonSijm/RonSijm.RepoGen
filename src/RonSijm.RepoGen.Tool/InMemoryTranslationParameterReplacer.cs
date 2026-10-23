using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

internal sealed class InMemoryTranslationParameterReplacer(IReadOnlyDictionary<ParameterExpression, Expression> bindings) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) => bindings.TryGetValue(node, out var value) ? value : node;
}
