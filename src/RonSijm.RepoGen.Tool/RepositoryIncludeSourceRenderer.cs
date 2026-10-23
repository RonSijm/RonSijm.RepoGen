using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Compiles typed loading chains against the final EF model, retaining filtered includes.</summary>
internal static class RepositoryIncludeSourceRenderer
{
    public static IncludeModel[] Create(IEntityType entity, IEnumerable<RepositoryIncludeDesign> includes,
        List<FilterParameterModel>? parameters = null)
    {
        var result = includes.Select(include => Create(entity, include, parameters))
            .DistinctBy(static include => include.ExpressionSource + "|" + string.Join("|", include.ThenIncludes ?? [])).ToArray();
        Validate(result);
        return result;
    }

    private static IncludeModel Create(IEntityType entity, RepositoryIncludeDesign include, List<FilterParameterModel>? parameters)
        => Create(entity, include.Navigation, include.ThenIncludes, parameters);

    public static IncludeModel CreateInferred(IEntityType entity, LambdaExpression navigation)
        => Create(entity, navigation, [], parameters: null);

    private static IncludeModel Create(IEntityType entity, LambdaExpression root, IReadOnlyList<LambdaExpression> continuations, List<FilterParameterModel>? parameters)
    {
        var paths = new List<IncludePathModel>();
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var expressions = new List<string>();
        var current = entity;
        var prefix = string.Empty;
        foreach (var navigation in new[] { root }.Concat(continuations))
        {
            var then = expressions.Count > 0;
            if (!current.ClrType.IsAssignableFrom(navigation.Parameters[0].Type))
            {
                throw Invalid(entity, $"ThenInclude must select a navigation on '{current.ClrType.FullName}' (the previous collection's element or reference), not '{navigation.Parameters[0].Type.FullName}'");
            }

            var body = navigation.Body;
            var filtered = false;
            while (body is MethodCallExpression call)
            {
                if (call.Method.DeclaringType != typeof(Enumerable) && call.Method.DeclaringType != typeof(Queryable) ||
                    call.Method.Name is not ("Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "Skip" or "Take"))
                {
                    throw Invalid(entity, $"'{call.Method.Name}' is not a supported filtered-include operator");
                }

                filtered = true;
                body = call.Arguments[0];
            }

            var core = body;
            var members = new Stack<MemberExpression>();
            while (body is MemberExpression member)
            {
                members.Push(member);
                body = member.Expression!;
            }

            if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast &&
                cast.Operand == navigation.Parameters[0] && entity.Model.FindEntityType(cast.Type) is { } derived &&
                current.ClrType.IsAssignableFrom(derived.ClrType))
            {
                current = derived;
            }
            else if (body != navigation.Parameters[0])
            {
                throw Invalid(entity, "an include must select a mapped navigation path rooted at its lambda parameter");
            }

            if (members.Count == 0)
            {
                throw Invalid(entity, "an include must select at least one mapped navigation");
            }

            var names = navigation.Parameters.Select((parameter, index) => index == 0
                ? "entity"
                : RepositoryModelFactory.ToParameterName(parameter.Name ?? "value" + index)).ToArray();
            if (navigation.Parameters.Count > 1)
            {
                if (parameters is null)
                {
                    throw Invalid(entity, "parameterized loading is only supported on named queries; attach the profile to HasQuery, not an entity default or standalone projection");
                }

                // Bind inputs without evaluating designer expressions or prematurely rendering nullable paths.
                RepositoryModelFactory.RenderBoundExpression(Expression.Lambda(Expression.Constant(true), navigation.Parameters), parameters, skipRow: true);
            }

            var targetSource = body == navigation.Parameters[0]
                ? then ? "entity!" : "entity"
                : "(" + ProjectionExpressionRenderer.RenderBody(Expression.Lambda(body, navigation.Parameters), names) + ")!";
            var lastWasCollection = false;
            foreach (var member in members)
            {
                if (lastWasCollection)
                {
                    throw Invalid(entity, "continue a collection with ThenInclude rather than traversing its members in one path");
                }

                var mapped = (INavigationBase?)current.FindNavigation(member.Member.Name) ?? current.FindSkipNavigation(member.Member.Name);
                if (mapped is null)
                {
                    throw Invalid(entity, $"'{current.ClrType.FullName}.{member.Member.Name}' is not a mapped navigation");
                }

                targetSource += (member == members.Peek() ? string.Empty : "!") + "." + RepositoryModelFactory.EscapeIdentifier(member.Member.Name);
                prefix = prefix.Length == 0 ? member.Member.Name : prefix + "." + member.Member.Name;
                paths.Add(new IncludePathModel(prefix, mapped.IsCollection, null));
                lastWasCollection = mapped.IsCollection;
                current = mapped.TargetEntityType;
                dependencies.Add(RepositoryModelFactory.CSharpTypeName(current.ClrType));
            }

            if (filtered && !lastWasCollection)
            {
                throw Invalid(entity, "filtered-include operators require a collection navigation");
            }

            var placeholder = Expression.Parameter(core.Type, "__repoGenNavigation");
            var rewritten = new ReplaceCore(core, placeholder).Visit(navigation.Body)!;
            var substitutions = new Dictionary<ParameterExpression, string> { [placeholder] = targetSource };
            var expression = (navigation.Parameters.Count == 1 ? "static " : string.Empty) + "entity => " +
                             ProjectionExpressionRenderer.RenderBody(Expression.Lambda(rewritten, navigation.Parameters), names, substitutions);
            expressions.Add(expression);
            if (filtered)
            {
                // Compare policies, not spelling: two branches can use different lambda names and
                // select the same collection through a dotted path or a ThenInclude continuation.
                var canonical = new CanonicalFilterParameters().Visit(rewritten)!;
                paths[^1] = paths[^1] with
                {
                    FilterSource = ProjectionExpressionRenderer.RenderBody(Expression.Lambda(canonical, navigation.Parameters), names,
                        new Dictionary<ParameterExpression, string> { [placeholder] = "collection" })
                };
            }

            dependencies.UnionWith(RepositoryQuerySourceRenderer.NavigationDependencies(entity.Model, navigation)
                .Select(static type => RepositoryModelFactory.CSharpTypeName(type)));
        }

        return new IncludeModel(expressions[0], expressions.Skip(1).ToArray(), paths,
            dependencies.Order(StringComparer.Ordinal).ToArray());
    }

    public static void Validate(IEnumerable<IncludeModel> includes, bool requireExplicitStrategy = false,
        RepositoryQuerySplittingBehavior splitting = RepositoryQuerySplittingBehavior.Unspecified)
    {
        var paths = includes.SelectMany(static include => include.Paths ?? []).ToArray();
        foreach (var group in paths.Where(static path => path.FilterSource is not null).GroupBy(static path => path.Path, StringComparer.Ordinal))
        {
            if (group.Select(static path => path.FilterSource).Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                throw new InvalidOperationException($"RGD1402: Navigation '{group.Key}' has conflicting filtered includes. Use one unique filter per navigation, including across load profiles.");
            }
        }

        if (requireExplicitStrategy && splitting == RepositoryQuerySplittingBehavior.Unspecified &&
            paths.Where(static path => path.IsCollection).Select(static path => path.Path).Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            throw new InvalidOperationException("RGD1403: Multiple collection loading paths require an explicit AsSingleQuery() or AsSplitQuery() policy when RequireExplicitLoadStrategy() is enabled.");
        }
    }

    public static string ApplyQuery(string source, IncludeModel include)
    {
        var result = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(" + source + ", " + include.ExpressionSource + ")";
        foreach (var then in include.ThenIncludes ?? [])
        {
            result = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ThenInclude(" + result + ", " + then + ")";
        }

        return result;
    }

    public static string ApplySelector(string selector, IncludeModel include) => include.ThenIncludes is not { Count: > 0 }
        ? selector + ".Include(" + include.ExpressionSource + ")"
        : selector + ".ConfigureQuery(__repoGenIncludeSource => " + ApplyQuery("__repoGenIncludeSource", include) + ")";

    private static InvalidOperationException Invalid(IEntityType entity, string message) =>
        new($"RGD1401: Loading configuration for '{entity.ClrType.FullName}': {message}.");

    private sealed class ReplaceCore(Expression core, ParameterExpression replacement) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) => node == core ? replacement : base.Visit(node);
    }

    private sealed class CanonicalFilterParameters : ExpressionVisitor
    {
        private readonly Dictionary<ParameterExpression, ParameterExpression> parameters = [];

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            var canonical = node.Parameters.Select(parameter =>
            {
                if (parameters.TryGetValue(parameter, out var existing))
                {
                    return existing;
                }

                var replacement = Expression.Parameter(parameter.Type, "item" + parameters.Count.ToString(CultureInfo.InvariantCulture));
                parameters.Add(parameter, replacement);
                return replacement;
            }).ToArray();
            return Expression.Lambda(node.Type, Visit(node.Body)!, node.TailCall, canonical);
        }

        protected override Expression VisitParameter(ParameterExpression node) => parameters.GetValueOrDefault(node, node);
    }
}