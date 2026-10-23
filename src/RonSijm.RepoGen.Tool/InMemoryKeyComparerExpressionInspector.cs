using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Tool;

/// <summary>Validates emitted comparer construction without invoking constructors, getters or delegates.</summary>
internal sealed class InMemoryKeyComparerExpressionInspector(ISet<string> reasons) : ExpressionVisitor
{
    public override Expression? Visit(Expression? node)
    {
        if (node is not null && node is not (NewExpression or NewArrayExpression or ConstantExpression or MemberExpression or UnaryExpression))
            reasons.Add("Comparer factories must be self-contained construction expressions; methods, delegates and ambient/captured state are not supported.");
        if (node is not null && ContainsInfrastructure(node.Type))
            reasons.Add("Comparer construction cannot reference EF or designer infrastructure types.");
        return base.Visit(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        var ordinal = node.Member.DeclaringType == typeof(StringComparer) &&
            node.Member.Name is nameof(StringComparer.Ordinal) or nameof(StringComparer.OrdinalIgnoreCase);
        var @default = node.Member.DeclaringType is { IsGenericType: true } type &&
            type.GetGenericTypeDefinition() == typeof(EqualityComparer<>) && node.Member.Name == nameof(EqualityComparer<int>.Default);
        if (node.Expression is not null || !(ordinal || @default || node.Member is FieldInfo { IsLiteral: true }))
            reasons.Add("Use StringComparer.Ordinal/OrdinalIgnoreCase, EqualityComparer<TKey>.Default, literals or public constructors; captured instances and culture-dependent/static factories are not evaluated.");
        return base.VisitMember(node);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value is not null && !(node.Type.IsPrimitive || node.Type.IsEnum || node.Type == typeof(string) || node.Type == typeof(decimal)))
            reasons.Add("Comparer construction arguments must be portable literals, not captured objects.");
        return base.VisitConstant(node);
    }

    protected override Expression VisitNew(NewExpression node)
    {
        if (node.Constructor is { IsPublic: false }) reasons.Add("Comparer construction requires public constructors usable in separate testing output.");
        return base.VisitNew(node);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType is not (ExpressionType.Convert or ExpressionType.ConvertChecked) || node.Method is not null)
            reasons.Add("Comparer construction supports ordinary casts only; custom conversion behavior requires an independent public constructor.");
        return base.VisitUnary(node);
    }

    private static bool ContainsInfrastructure(Type type) =>
        InfrastructureHierarchy(type) ||
        type.HasElementType && ContainsInfrastructure(type.GetElementType()!) ||
        type.IsGenericType && type.GetGenericArguments().Any(ContainsInfrastructure);

    private static bool InfrastructureHierarchy(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            if (InfrastructureAssembly(current.Assembly.GetName().Name) || current.GetInterfaces().Any(static contract => InfrastructureAssembly(contract.Assembly.GetName().Name)))
                return true;
        return false;
    }

    private static bool InfrastructureAssembly(string? assembly) => assembly is not null &&
        (assembly.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || assembly is
            "RonSijm.RepoGen.Design" or "RonSijm.RepoGen.Generation" or "RonSijm.RepoGen.Tool" or "RonSijm.RepoGen.Validation" or
            "RonSijm.RepoGen.EntityFrameworkCore" or "RonSijm.RepoGen.Testing.EntityFrameworkCore" or "RonSijm.RepoGen.Sqlite" or "RonSijm.RepoGen.SqlServer");
}
