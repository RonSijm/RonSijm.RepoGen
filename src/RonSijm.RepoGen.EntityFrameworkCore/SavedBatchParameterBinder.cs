using System.Linq.Expressions;

namespace RonSijm.RepoGen.EntityFrameworkCore;

internal sealed class SavedBatchParameterBinder(ParameterExpression parameter, Expression value) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? value : node;
}
