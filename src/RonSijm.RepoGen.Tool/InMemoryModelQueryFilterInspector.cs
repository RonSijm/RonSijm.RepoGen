using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Requires ordinary mapped scalar access, not a captured context or an entity graph.</summary>
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
}
