using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Replaces exact captured-context member symbols without evaluating any captured object or getter.</summary>
internal sealed class InMemoryModelFilterContextExpressions(
    IReadOnlyList<RepositoryInMemoryModelFilterContextDesign> declarations, ParameterExpression key) : ExpressionVisitor
{
    internal const string KeyVariable = "__repoGenModelFilterContextKey";
    private readonly HashSet<RepositoryInMemoryModelFilterContextDesign> used = [];

    internal static void Validate(DbContext context, RepositoryDesign? design)
    {
        if (design is null || !design.Options.InMemoryTestQueries && !design.Options.InMemoryTestCommands) return;
        foreach (var binding in design.InMemoryModelFilterContexts)
        {
            if (!binding.ContextType.IsAssignableFrom(context.GetType()) || !typeof(DbContext).IsAssignableFrom(binding.ContextType) ||
                design.Options.ContextKeyType != binding.ContextKeyType || !binding.ContextKeyType.IsVisible ||
                typeof(DbContext).IsAssignableFrom(binding.ContextKeyType) || context.Model.FindEntityType(binding.ContextKeyType) is not null ||
                !InMemoryTranslationExpressions.Scalar(binding.ContextValue.ReturnType))
                throw new InvalidOperationException("RGD4701: Fixture model-filter bindings require this DbContext's scalar member and the exact public, non-entity UseKeyedContextLeases key type.");
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            new InMemoryQueryExpressionInspector(reasons).Visit(binding.KeyValue);
            new InMemoryTranslationReplacementInspector(reasons).Visit(binding.KeyValue);
            new InMemoryModelFilterContextInspector(binding.KeyValue.Parameters[0], reasons).Visit(binding.KeyValue);
            try { ProjectionExpressionRenderer.Render(binding.KeyValue); }
            catch (InvalidOperationException exception) { reasons.Add(exception.Message); }
            catch (NotSupportedException exception) { reasons.Add(exception.Message); }
            if (reasons.Count != 0)
                throw new InvalidOperationException("RGD4702: Fixture model-filter context values must be self-contained portable scalar expressions: " + string.Join(" ", reasons));
        }
    }

    internal IReadOnlyList<InMemoryModelFilterContextBindingModel> Receipts => used
        .OrderBy(static binding => binding.ContextType.FullName, StringComparer.Ordinal)
        .ThenBy(static binding => binding.Member.Name, StringComparer.Ordinal)
        .Select(static binding => new InMemoryModelFilterContextBindingModel(
            RepositoryModelFactory.CSharpTypeName(binding.ContextType), binding.Member.Name,
            RepositoryModelFactory.CSharpTypeName(binding.ContextKeyType), RepositoryModelFactory.CSharpTypeName(binding.KeyValue.ReturnType),
            ProjectionExpressionRenderer.RenderBody(binding.KeyValue, ["contextKey"]))).ToArray();

    internal LambdaExpression Bind(LambdaExpression filter)
    {
        var body = Visit(filter.Body)!;
        return used.Count == 0 ? filter : Expression.Lambda(body, [.. filter.Parameters, key]);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ConstantExpression { Value: { } instance })
        {
            var binding = declarations.SingleOrDefault(binding => binding.Member == node.Member && binding.ContextType.IsInstanceOfType(instance));
            if (binding is not null)
            {
                used.Add(binding);
                return new InMemoryTranslationParameterReplacer(new Dictionary<ParameterExpression, Expression>
                    { [binding.KeyValue.Parameters[0]] = key }).Visit(binding.KeyValue.Body)!;
            }
        }
        return base.VisitMember(node);
    }
}
