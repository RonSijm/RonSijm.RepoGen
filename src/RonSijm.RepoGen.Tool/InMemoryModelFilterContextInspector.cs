using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Tool;

/// <summary>Allows public scalar key slots, not key identity, service graphs or ambient state.</summary>
internal sealed class InMemoryModelFilterContextInspector(ParameterExpression key, ISet<string> reasons) : ExpressionVisitor
{
    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        Visit(node.Body);
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node != key || !InMemoryTranslationExpressions.Scalar(node.Type))
            reasons.Add("Read public scalar context-key members rather than whole-key identity or other parameters.");
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression != key) return base.VisitMember(node);
        if (!InMemoryTranslationExpressions.Scalar(node.Type) ||
            node.Member is not PropertyInfo { GetMethod.IsPublic: true, GetMethod.IsStatic: false } and
                not FieldInfo { IsPublic: true, IsStatic: false })
            reasons.Add("Context-key values require direct public readable scalar members; nested service/object graphs are not fixture bindings.");
        return node;
    }
}
