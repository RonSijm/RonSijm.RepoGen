using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

/// <summary>Conservative portable expression boundary; never executes a designer expression during inspection.</summary>
internal sealed class InMemoryQueryExpressionInspector(ISet<string> reasons) : ExpressionVisitor
{
    internal static bool PortableMethod(System.Reflection.MethodInfo method)
    {
        var type = method.DeclaringType;
        return type == typeof(Enumerable) || type == typeof(Array) && method.Name == nameof(Array.Empty) || type == typeof(string) || type == typeof(Math) || type == typeof(MathF) ||
               type == typeof(Convert) || type == typeof(decimal) || type == typeof(TimeSpan) ||
               type?.FullName == "RonSijm.RepoGen.Testing.InMemoryStringPatterns" &&
                   type.Assembly.GetName().Name == "RonSijm.RepoGen.Testing" && method.Name == "Like" ||
               type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
               type == typeof(Guid) && method.Name != nameof(Guid.NewGuid) ||
               type == typeof(object) && method.Name is nameof(object.Equals) or nameof(object.ToString) ||
               type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(Nullable<>) ||
               type?.IsGenericType == true && method.Name == "Contains" && type.GetGenericTypeDefinition() is var collection &&
                   (collection == typeof(List<>) || collection == typeof(HashSet<>) || collection == typeof(ICollection<>)) ||
               type?.IsPrimitive == true || type == typeof(Enum) && method.Name == nameof(Enum.ToString);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (InMemoryPropertyRead.TryRead(node, out var receiver, out _))
        {
            Visit(receiver);
            return node;
        }
        if (!PortableMethod(node.Method))
            reasons.Add("Method '" + node.Method.DeclaringType?.FullName + "." + node.Method.Name + "' is outside the portable expression subset; use a typed script.");
        return base.VisitMethodCall(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        var constant = node.Member.DeclaringType == typeof(string) && node.Member.Name == nameof(string.Empty) ||
                       node.Member.DeclaringType == typeof(Guid) && node.Member.Name == nameof(Guid.Empty) ||
                       node.Member.DeclaringType == typeof(TimeSpan) && node.Member.Name == nameof(TimeSpan.Zero) ||
                       node.Member.DeclaringType == typeof(DateTime) && node.Member.Name is nameof(DateTime.MinValue) or nameof(DateTime.MaxValue);
        if (node.Expression is null && !constant && node.Member is not System.Reflection.FieldInfo { IsLiteral: true })
            reasons.Add("Static member '" + node.Member.DeclaringType?.FullName + "." + node.Member.Name + "' requires explicit test behavior rather than an ambient clock/state dependency.");
        return base.VisitMember(node);
    }

    protected override Expression VisitInvocation(InvocationExpression node)
    {
        reasons.Add("Invoked delegates are outside the portable expression subset; use a typed script.");
        return base.VisitInvocation(node);
    }

    protected override Expression VisitIndex(IndexExpression node)
    {
        reasons.Add("Indexed property access needs an exact MapInMemoryProperty binding; dynamic/unbound indexers require a typed script.");
        return base.VisitIndex(node);
    }

    protected override Expression VisitNew(NewExpression node)
    {
        if (!node.Type.IsVisible && !ProjectionExpressionRenderer.IsAnonymousType(node.Type))
            reasons.Add("Constructed type '" + node.Type.FullName + "' is not public in the separate testing output.");
        return base.VisitNew(node);
    }
}
