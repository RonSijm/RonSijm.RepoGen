using System.Linq.Expressions;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryQueryRenderingModel CreateInMemoryTranslatedQuery(RepositoryEntityDesign entity,
        RepositoryQueryDesign query, ConfiguredQueryModel configured, InMemoryTranslationExpressions translations,
        Type resultType, RepositoryTypeNullabilityDesign? resultNullability)
    {
        var parameters = configured.Parameters.ToList();
        LambdaExpression Expand(LambdaExpression expression) => translations.Expand(expression);
        string Bound(LambdaExpression expression, bool skipRow, bool lambda = true, string rowName = "entity",
            RepositoryTypeNullabilityDesign? annotation = null)
            => RenderBoundExpression(Expand(expression), parameters, skipRow, lambda, rowName, resultNullability: annotation);
        var membershipDesigns = query.Memberships.Concat(query.ResultStages.Select(static stage => stage.Membership).OfType<RepositoryMembershipDesign>()).ToArray();
        var memberships = membershipDesigns.Select((membership, index) => CreateMembership(membership, index, parameters, cacheEnabled: false, translations)).ToArray();
        var predicates = CreateConfiguredPredicates(query.Predicates.Select(Expand), out _)
            .Concat(memberships.Take(query.Memberships.Count).Select(static membership => new ConfiguredPredicateModel(membership.PredicateSource, membership.ApplyName)))
            .Concat(query.ConditionalPredicates.Select(predicate => new ConfiguredPredicateModel(Bound(predicate.Predicate, true, false), Bound(predicate.Condition, false)))).ToArray();
        var usePipeline = configured.Pipeline is not null;
        var projection = query.Projection;
        var projectionAnnotation = projection?.Expression.ReturnType == resultNullability?.Type ? resultNullability : projection?.ResultNullability;
        var projectionExpression = projection is null ? configured.ProjectionExpressionSource : ProjectionExpressionRenderer.Render(Expand(projection.Expression), projectionAnnotation);
        var projectionBody = projection is null ? configured.ProjectionBodySource : ProjectionExpressionRenderer.RenderBody(Expand(projection.Expression), ["entity"], resultNullability: projectionAnnotation);
        if (!usePipeline && projection is not null && query.ResultKind is RepositoryQueryResultKind.SingleOrDefault or RepositoryQueryResultKind.FirstOrDefault)
            projectionExpression = RenderNullableProjection(Expand(projection.Expression), projection.ResultNullability);
        var outputType = configured.Dictionary?.ValueTypeName ?? configured.Scalar?.ReturnTypeName ?? configured.ResultTypeName;
        if (!usePipeline && resultNullability is not null && query.ResultKind is not (RepositoryQueryResultKind.Count or RepositoryQueryResultKind.Exists or RepositoryQueryResultKind.GroupedList))
        {
            var projectedType = query.ResultKind is RepositoryQueryResultKind.SingleOrDefault or RepositoryQueryResultKind.FirstOrDefault &&
                resultType.IsValueType && Nullable.GetUnderlyingType(resultType) is null ? outputType + "?" : outputType;
            projectionExpression = "entity => (" + projectedType + ")(" + projectionBody + ")";
        }
        var group = query.Group is null ? null : new ConfiguredGroupedQueryModel(
            query.Group.KeySelector.Parameters.Count == 1 ? ProjectionExpressionRenderer.Render(Expand(query.Group.KeySelector)) : Bound(query.Group.KeySelector, true),
            !usePipeline && resultNullability is not null
                ? "group => " + AnnotateResultExpression(resultNullability, outputType, Bound(query.Group.Projection, true, false, "group", resultNullability))
                : query.Group.Projection.Parameters.Count == 1
                    ? ProjectionExpressionRenderer.Render(Expand(query.Group.Projection), query.Group.ResultType == resultNullability?.Type ? resultNullability : null)
                    : Bound(query.Group.Projection, true, rowName: "group", annotation: query.Group.ResultType == resultNullability?.Type ? resultNullability : null));
        var dictionary = query.Dictionary is null ? null : configured.Dictionary! with
        {
            KeyBodySource = AnnotateResultExpression(query.DictionaryKeyNullability, configured.Dictionary!.KeyTypeName,
                Bound(query.Dictionary.KeySelector, true, false, annotation: query.DictionaryKeyNullability)),
            ValueBodySource = AnnotateResultExpression(resultNullability, outputType, Bound(query.Dictionary.ValueSelector, true, false, annotation: resultNullability))
        };
        var scalar = query.Scalar is null ? null : configured.Scalar! with
        {
            SelectorExpressionSource = resultNullability is null
                ? query.Scalar.Selector.Parameters.Count == 1 ? ProjectionExpressionRenderer.Render(Expand(query.Scalar.Selector)) : Bound(query.Scalar.Selector, true)
                : "entity => " + AnnotateResultExpression(resultNullability, outputType, Bound(query.Scalar.Selector, true, false, annotation: resultNullability))
        };
        var cursorValidations = query.CursorValidations.Zip(configured.CursorValidations ?? []).Select(pair => pair.Second with
        {
            PredicateSource = ProjectionExpressionRenderer.RenderBody(Expand(pair.First.Predicate),
                ["cursorPage.Cursor!", .. pair.First.Predicate.Parameters.Skip(1).Select(parameter => ToParameterName(parameter.Name ?? "value"))])
        }).ToArray();
        var rendering = configured with
        {
            Predicates = predicates,
            Sorts = query.Sorts.Select(sort => new SortModel(sort.IsDescending, sort.KeySelector.Parameters.Count == 1
                ? ProjectionExpressionRenderer.Render(Expand(sort.KeySelector)) : Bound(sort.KeySelector, true))).ToArray(),
            ProjectionExpressionSource = projectionExpression,
            ProjectionBodySource = projectionBody,
            Group = group,
            Pipeline = usePipeline ? CreateResultPipeline(query, resultType, parameters, resultNullability,
                membershipDesigns.Zip(memberships).ToDictionary(static pair => pair.First, static pair => pair.Second), translations) : null,
            Dictionary = dictionary,
            Scalar = scalar,
            Cursor = CreateCursor(query.Cursor, parameters, translations),
            ContextKeySource = query.ContextKey is null ? null : Bound(query.ContextKey, false),
            Validations = query.Validations.Zip(configured.Validations ?? []).Select(pair => pair.Second with { PredicateSource = Bound(pair.First.Predicate, false) }).ToArray(),
            CursorValidations = cursorValidations.Length == 0 ? null : cursorValidations,
            Memberships = memberships.Length == 0 ? null : memberships
        };
        return new(rendering,
            query.Source is not null || !query.UseDefaultSort ? [] : entity.DefaultSorts.Select(sort => new SortModel(sort.IsDescending, ProjectionExpressionRenderer.Render(Expand(sort.KeySelector)))).ToArray(),
            query.Source is not null ? [] : entity.VisibilityPolicies.Where(policy => !query.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal))
                .Select(policy => new VisibilityPolicyModel(policy.Name, ProjectionExpressionRenderer.RenderBody(Expand(policy.Predicate), ["entity"]))).ToArray());
    }
}
