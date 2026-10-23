using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Recognizes bounded null-safe scalar/DTO pipelines on an explicitly null-preserving skip navigation.</summary>
internal sealed class InMemoryNullSafeJoinUsageInspector(ISkipNavigation navigation) : ExpressionVisitor
{
    internal bool Supported { get; private set; } = true;
    private sealed record Sequence(bool EntityElements, bool MayBeNull);

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.Count > 0 && ReadSequence(node.Arguments[0]) is { } source)
        {
            if (node.Method.DeclaringType != typeof(Enumerable)) { Supported = false; return node; }
            var name = node.Method.Name;
            if (name is nameof(Enumerable.Where) or nameof(Enumerable.Select) or nameof(Enumerable.OrderBy) or
                nameof(Enumerable.OrderByDescending) or nameof(Enumerable.ThenBy) or nameof(Enumerable.ThenByDescending) or
                nameof(Enumerable.Skip) or nameof(Enumerable.Take) or nameof(Enumerable.Distinct))
            {
                _ = ReadSequence(node);
                return node;
            }
            if (name is nameof(Enumerable.Count) or nameof(Enumerable.LongCount) or nameof(Enumerable.Any) or nameof(Enumerable.All))
            {
                if (node.Arguments.Count == 2) CheckLambda(node.Arguments[1], source);
                else if (node.Arguments.Count != 1 || name == nameof(Enumerable.All)) Supported = false;
                return node;
            }
            if (!source.EntityElements && name is nameof(Enumerable.ToList) or nameof(Enumerable.ToArray) or
                nameof(Enumerable.First) or nameof(Enumerable.FirstOrDefault) or nameof(Enumerable.Single) or nameof(Enumerable.SingleOrDefault) or
                nameof(Enumerable.Min) or nameof(Enumerable.Max) or nameof(Enumerable.Sum) or nameof(Enumerable.Average) or nameof(Enumerable.Contains))
            {
                // Scalar/DTO values no longer carry a possibly null entity into downstream operations.
                foreach (var argument in node.Arguments.Skip(1)) Visit(argument);
                return node;
            }
            Supported = false;
            return node;
        }
        return base.VisitMethodCall(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Member.Name == "Count" && node.Expression is not null && ReadSequence(node.Expression) is not null) return node;
        if (Matches(node)) Supported = false; // Raw entity collections/Include selectors cannot escape the checked pipeline.
        return base.VisitMember(node);
    }

    private Sequence? ReadSequence(Expression expression)
    {
        expression = InMemoryNullableTargetExpressionInspector.StripConvert(expression);
        if (expression is MemberExpression member && Matches(member))
        {
            Visit(member.Expression);
            return new(true, true);
        }
        if (expression is not MethodCallExpression call || call.Arguments.Count == 0 || ReadSequence(call.Arguments[0]) is not { } source) return null;
        if (call.Method.DeclaringType != typeof(Enumerable)) { Supported = false; return source; }
        var name = call.Method.Name;
        if (name is nameof(Enumerable.Where) or nameof(Enumerable.Select) or nameof(Enumerable.OrderBy) or
            nameof(Enumerable.OrderByDescending) or nameof(Enumerable.ThenBy) or nameof(Enumerable.ThenByDescending))
        {
            if (call.Arguments.Count != 2) { Supported = false; return source; }
            var lambda = CheckLambda(call.Arguments[1], source);
            if (lambda is null) return source;
            if (name == nameof(Enumerable.Where))
                return source with { MayBeNull = source.MayBeNull && !InMemoryNullableTargetExpressionInspector.NonNullWhen(lambda.Body, lambda.Parameters[0], whenTrue: true) };
            if (name == nameof(Enumerable.Select))
            {
                if (ContainsModel(lambda.ReturnType)) Supported = false;
                return new(false, false);
            }
            return source;
        }
        if (name is nameof(Enumerable.Skip) or nameof(Enumerable.Take) && call.Arguments.Count == 2)
        {
            Visit(call.Arguments[1]);
            return source;
        }
        if (name == nameof(Enumerable.Distinct) && !source.EntityElements && call.Arguments.Count == 1) return source;
        // Do not silently accept an unknown operator by just visiting its guarded source.
        Supported = false;
        return source;
    }

    private LambdaExpression? CheckLambda(Expression expression, Sequence source)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote) expression = quote.Operand;
        if (expression is not LambdaExpression { Parameters.Count: 1 } lambda) { Supported = false; return null; }
        if (source.EntityElements)
        {
            var safety = new InMemoryNullableTargetExpressionInspector(lambda.Parameters[0], source.MayBeNull);
            safety.Visit(lambda.Body);
            Supported &= safety.Supported;
        }
        Visit(lambda.Body); // Other occurrences, including reused expression-DAG members, need their own proof.
        return lambda;
    }

    private bool Matches(MemberExpression member) => member.Member.Name == navigation.Name && member.Expression is not null &&
        navigation.DeclaringEntityType.Model.FindEntityType(member.Expression.Type) == navigation.DeclaringEntityType;

    private bool ContainsModel(Type type) => navigation.DeclaringEntityType.Model.FindEntityType(type) is not null ||
        type.HasElementType && ContainsModel(type.GetElementType()!) || type.IsGenericType && type.GetGenericArguments().Any(ContainsModel);
}
