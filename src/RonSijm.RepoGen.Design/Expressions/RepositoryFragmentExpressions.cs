using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Inlines design-time fragments, without invocation nodes or compiled delegates.</summary>
internal static class RepositoryFragmentExpressions
{
    internal static LambdaExpression Combine(LambdaExpression left, LambdaExpression right, bool conjunction)
    {
        var parameters = left.Parameters.Count == 2
            ? left.Parameters.ToArray()
            : right.Parameters.Count == 2
                ? new[] { left.Parameters[0], right.Parameters[1] }
                : left.Parameters.ToArray();
        var body = Rebind(right, parameters).Body;
        return Expression.Lambda(conjunction ? Expression.AndAlso(left.Body, body) : Expression.OrElse(left.Body, body), parameters);
    }

    internal static LambdaExpression Negate(LambdaExpression predicate) =>
        Expression.Lambda(Expression.Not(predicate.Body), predicate.Parameters);

    internal static LambdaExpression BindRequest(LambdaExpression predicate, ParameterExpression request) =>
        Rebind(predicate, new[] { predicate.Parameters[0], request });

    internal static LambdaExpression BindInput(LambdaExpression expression, ParameterExpression input) =>
        Rebind(expression, new[] { input });

    internal static LambdaExpression RenameRequest(LambdaExpression predicate, string requestName)
    {
        if (string.IsNullOrWhiteSpace(requestName))
        {
            throw new ArgumentException("RGD2202: A fragment request name must be a C# identifier.", nameof(requestName));
        }

        var name = requestName.Trim();
        if (!(char.IsLetter(name[0]) || name[0] == '_') || name.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("RGD2202: A fragment request name must be a C# identifier.", nameof(requestName));
        }

        return BindRequest(predicate, Expression.Parameter(predicate.Parameters[1].Type, name));
    }

    private static LambdaExpression Rebind(LambdaExpression predicate, ParameterExpression[] parameters)
    {
        var replacements = new Dictionary<ParameterExpression, ParameterExpression>();
        for (var index = 0; index < predicate.Parameters.Count; index++)
        {
            replacements.Add(predicate.Parameters[index], parameters[index]);
        }

        return Expression.Lambda(new ParameterRebinder(replacements).Visit(predicate.Body)!, parameters);
    }

    private sealed class ParameterRebinder(IReadOnlyDictionary<ParameterExpression, ParameterExpression> replacements) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            replacements.TryGetValue(node, out var replacement) ? replacement : node;

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            // Only free root references are substituted. Preserve nested bindings and avoid capture
            // even when a hand-built expression reuses a parameter object as a nested binder.
            var nested = replacements.Where(pair => !node.Parameters.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            var parameters = node.Parameters.Select(parameter =>
            {
                if (!nested.ContainsValue(parameter))
                {
                    return parameter;
                }

                var renamed = Expression.Parameter(parameter.Type, parameter.Name);
                nested.Add(parameter, renamed);
                return renamed;
            }).ToArray();
            return node.Update(new ParameterRebinder(nested).Visit(node.Body)!, parameters);
        }
    }
}
