using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>A scalar adapter cannot sneak persistence entity construction/types into its assignment values.</summary>
internal sealed class InMemoryMappingValueExpressionInspector(IModel model, ISet<string> reasons, Type? scalarReceiver = null,
    bool allowScalarSequences = false) : ExpressionVisitor
{
    private readonly HashSet<Type> entities = model.GetEntityTypes().Select(static entity => entity.ClrType).ToHashSet();

    internal bool ContainsEntity(Type type) => entities.Contains(type) ||
        type.HasElementType && ContainsEntity(type.GetElementType()!) ||
        type.IsGenericType && type.GetGenericArguments().Any(ContainsEntity);

    public override Expression? Visit(Expression? node)
    {
        if (node is ParameterExpression parameter && parameter.Type == scalarReceiver) return node;
        // Inferred LINQ and lambda signatures may carry the row type without emitting its CLR name.
        // The companion identity inspector still rejects whole-row values, casts and entity behavior.
        if (allowScalarSequences && node is not null && ContainsReceiver(node.Type) &&
            (node is ParameterExpression or LambdaExpression || node is MethodCallExpression call && ProjectionExpressionRenderer.CanInferLinqArguments(call)))
            return base.Visit(node);
        if (node is not null && ContainsEntity(node.Type)) Reject();
        if (node is not null && !IsPublicValueType(node.Type)) Inaccessible();
        return base.Visit(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Member.DeclaringType is { } type && !(type == scalarReceiver && node.Expression is ParameterExpression))
        {
            if (ContainsEntity(type)) Reject();
            if (!IsPublicValueType(type)) Inaccessible();
        }
        return base.VisitMember(node);
    }

    private void Reject() => reasons.Add("Scalar mapping values cannot construct, carry or reference persistence entity types. Use independent public scalar data.");

    private bool ContainsReceiver(Type type) => type == scalarReceiver ||
        type.HasElementType && ContainsReceiver(type.GetElementType()!) ||
        type.IsGenericType && type.GetGenericArguments().Any(ContainsReceiver);

    private static bool IsPublicValueType(Type type) => type.IsVisible ||
        ProjectionExpressionRenderer.IsAnonymousType(type) && type.GetGenericArguments().All(IsPublicValueType) ||
        type.HasElementType && IsPublicValueType(type.GetElementType()!) ||
        type.IsGenericType && type.GetGenericTypeDefinition().IsVisible && type.GetGenericArguments().All(IsPublicValueType);

    private void Inaccessible() => reasons.Add("Scalar mapping values must use public types; private constants/casts cannot leak into separate testing output.");
}
