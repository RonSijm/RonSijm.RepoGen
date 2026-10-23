using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

internal sealed class ReturningProjectionValidator(IEntityType entity, ParameterExpression row, bool includeShadowProperties, bool includeMappedProperties = false) : ExpressionVisitor
{
    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression != row) return base.VisitMember(node);
        if ((includeMappedProperties ? ReturningProjectionRewriter.Member(entity, node) : entity.FindProperty(node.Member.Name)) is null)
            throw new InvalidOperationException($"RGD1306: Returning projection '{node.Member.Name}' is not a mapped scalar. Native returned rows do not load navigations.");
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!ReturningProjectionRewriter.IsProperty(node)) return base.VisitMethodCall(node);
        if (!includeShadowProperties && !includeMappedProperties)
            throw new InvalidOperationException("RGD1306: Returned EF.Property values require explicit IncludeShadowProperties permission.");
        _ = ReturningProjectionRewriter.Property(entity, row, node);
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node == row)
            throw new InvalidOperationException("RGD1306: Returning projections must use mapped scalar values, not pass the entity to a mapping helper or return an entity graph.");
        return base.VisitParameter(node);
    }
}
