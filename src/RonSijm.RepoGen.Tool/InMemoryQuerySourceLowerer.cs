using System.Linq.Expressions;
using System.Reflection;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Rebinds typed Set roots and standard query operators without compiling or invoking designer expressions.</summary>
internal sealed class InMemoryQuerySourceLowerer(ParameterExpression sources) : ExpressionVisitor
{
    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        nameof(Queryable.Where), nameof(Queryable.Select), nameof(Queryable.SelectMany),
        nameof(Queryable.Join), nameof(Queryable.GroupJoin), nameof(Queryable.GroupBy),
        nameof(Queryable.OrderBy), nameof(Queryable.OrderByDescending), nameof(Queryable.ThenBy), nameof(Queryable.ThenByDescending),
        nameof(Queryable.Distinct), nameof(Queryable.DefaultIfEmpty), nameof(Queryable.Concat), nameof(Queryable.Union),
        nameof(Queryable.Except), nameof(Queryable.Intersect), nameof(Queryable.Any), nameof(Queryable.All),
        nameof(Queryable.Count), nameof(Queryable.LongCount), nameof(Queryable.Contains), nameof(Queryable.Sum),
        nameof(Queryable.Min), nameof(Queryable.Max), nameof(Queryable.Average), nameof(Queryable.Skip), nameof(Queryable.Take)
    };

    private static readonly ILookup<string, MethodInfo> EnumerableMethods = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .ToLookup(static method => method.Name, StringComparer.Ordinal);

    internal Dictionary<Type, ParameterExpression> Roots { get; } = [];
    internal HashSet<string> UsedOperators { get; } = new(StringComparer.Ordinal);

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(RepositoryQuerySources) && node.Method.Name == nameof(RepositoryQuerySources.Set))
        {
            if (node.Object != sources) throw Invalid("Set must use the declared sources parameter");
            var type = node.Method.GetGenericArguments()[0];
            if (!Roots.TryGetValue(type, out var root))
            {
                root = Expression.Parameter(typeof(IEnumerable<>).MakeGenericType(type), "__repoGenSource" + Roots.Count);
                Roots.Add(type, root);
            }
            return root;
        }

        if (node.Method.DeclaringType != typeof(Queryable)) return base.VisitMethodCall(node);
        if (!Operators.Contains(node.Method.Name)) throw Invalid("Queryable." + node.Method.Name + " is outside the portable source operator subset");
        var arguments = node.Arguments.Select(argument => Visit(argument)!).ToArray();
        var typeArguments = node.Method.IsGenericMethod ? node.Method.GetGenericArguments() : Type.EmptyTypes;
        foreach (var definition in EnumerableMethods[node.Method.Name])
        {
            if (definition.GetParameters().Length != arguments.Length || definition.GetGenericArguments().Length != typeArguments.Length) continue;
            MethodInfo candidate;
            try { candidate = definition.IsGenericMethodDefinition ? definition.MakeGenericMethod(typeArguments) : definition; }
            catch (ArgumentException) { continue; }
            var parameters = candidate.GetParameters();
            if (!parameters.Select((parameter, index) => parameter.ParameterType.IsAssignableFrom(arguments[index].Type)).All(static matches => matches)) continue;
            UsedOperators.Add(node.Method.Name);
            return Expression.Call(candidate, arguments);
        }
        throw Invalid("Queryable." + node.Method.Name + " has no matching portable Enumerable overload");
    }

    protected override Expression VisitUnary(UnaryExpression node) => node.NodeType == ExpressionType.Quote
        ? Visit(node.Operand)!
        : base.VisitUnary(node);

    protected override Expression VisitLambda<T>(Expression<T> node) => Expression.Lambda(Visit(node.Body)!, node.Parameters);

    protected override Expression VisitConditional(ConditionalExpression node) => typeof(IQueryable).IsAssignableFrom(node.Type)
        ? Expression.Condition(Visit(node.Test)!, Visit(node.IfTrue)!, Visit(node.IfFalse)!)
        : base.VisitConditional(node);

    private static InvalidOperationException Invalid(string message) => new("Portable From source: " + message + ".");
}
