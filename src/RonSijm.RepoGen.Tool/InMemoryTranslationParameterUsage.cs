using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

internal sealed class InMemoryTranslationParameterUsage : ExpressionVisitor
{
    internal HashSet<ParameterExpression> Parameters { get; } = [];
    protected override Expression VisitParameter(ParameterExpression node) { Parameters.Add(node); return node; }
    protected override Expression VisitLambda<T>(Expression<T> node) { Visit(node.Body); return node; }
}
