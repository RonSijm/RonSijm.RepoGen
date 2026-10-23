using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Requires mapped scalar access or explicitly bound scalar values, not captured state or an entity graph.</summary>
internal sealed class InMemoryModelQueryFilterInspector(IEntityType entity, ParameterExpression row, ISet<string> reasons) : ExpressionVisitor
{
    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        Visit(node.Body);
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node == row)
            reasons.Add("Whole-row identity/behavior is not a portable model query filter; read mapped scalar members only.");
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression != row) return base.VisitMember(node);
        var property = entity.GetProperties().SingleOrDefault(property => property.PropertyInfo == node.Member);
        if (property?.PropertyInfo?.GetMethod?.IsPublic != true || !InMemoryTranslationExpressions.Scalar(property.ClrType))
            reasons.Add("Model query filters require public ordinary mapped scalar properties; navigation, shadow, field-only and unmapped members are not lowered.");
        return node; // The parameter is a receiver, not whole-row identity.
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (InMemoryPropertyRead.TryRead(node, out var receiver, out _))
        {
            if (receiver != row || !InMemoryTranslationExpressions.Scalar(node.Type))
            {
                reasons.Add("A mapped property filter must read one scalar from its own row parameter.");
            }
            return node;
        }
        return base.VisitMethodCall(node);
    }
}
