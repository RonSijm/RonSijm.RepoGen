using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

internal sealed class InMemoryTranslationReplacementInspector(ISet<string> reasons, HashSet<Type>? allowedDtos = null,
    bool allowCollections = false) : ExpressionVisitor
{
    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value is not null && !InMemoryTranslationExpressions.Scalar(node.Type))
            reasons.Add("Captured objects/services/collections and type metadata are not evaluated in a fixture translation.");
        return node;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        if (!InMemoryTranslationExpressions.Scalar(node.Type) && allowedDtos?.Contains(node.Type) != true &&
            !(allowCollections && allowedDtos is not null && InMemoryProjectionDataShape.List(node.Type) && InMemoryProjectionDataShape.ValueType(node.Type, allowedDtos)))
            reasons.Add("Translation replacements cannot construct entity, service or arbitrary object graphs.");
        return base.VisitNew(node);
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.Method is not null && !InMemoryQueryExpressionInspector.PortableMethod(node.Method))
            reasons.Add("Custom operators require an explicit method translation rather than executing arbitrary behavior.");
        return base.VisitBinary(node);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.Method is not null && !InMemoryQueryExpressionInspector.PortableMethod(node.Method))
            reasons.Add("Custom conversions require explicit fixture behavior.");
        return base.VisitUnary(node);
    }
}
