using System.Linq.Expressions;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryCommandRenderingModel CreateInMemoryTranslatedCommand(RepositoryEntityDesign entity,
        RepositoryCommandDesign command, ConfiguredCommandModel configured, InMemoryTranslationExpressions translations, RepositoryDesign fullDesign)
    {
        var parameters = configured.Parameters.ToList();
        LambdaExpression Expand(LambdaExpression expression) => translations.Expand(expression);
        string Bound(LambdaExpression expression, bool skipRow, bool lambda = false)
            => RenderBoundExpression(Expand(expression), parameters, skipRow, lambda);
        string Item(LambdaExpression expression, bool hasEntity, string rowName)
            => ProjectionExpressionRenderer.RenderBody(Expand(expression),
                expression.Parameters.Select((_, index) => hasEntity && index == 0 ? rowName : "item").ToArray());
        var visibility = (command.Kind != RepositoryCommandKind.Create || command.ResultFromSaved ? entity.VisibilityPolicies : [])
            .Where(policy => !command.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal))
            .Select(policy => new VisibilityPolicyModel(policy.Name, ProjectionExpressionRenderer.RenderBody(Expand(policy.Predicate), ["entity"]))).ToArray();
        var predicates = CreateConfiguredPredicates(command.Predicates.Select(Expand), out _).AsEnumerable();
        if (command.Kind != RepositoryCommandKind.Create)
            predicates = visibility.Select(static policy => new ConfiguredPredicateModel(policy.PredicateSource)).Concat(predicates);
        // Immediate renderers replace the appended token predicates with explicit structural equality.
        if (!configured.IsTracked && configured.ExpectedVersions is { Count: > 0 } versions)
            predicates = predicates.Concat(configured.Predicates.TakeLast(versions.Count));
        var updates = command.Updates.Zip(configured.Updates).Select(pair => pair.Second with
        {
            ValueExpressionSource = Bound(pair.First.Value, true, true),
            ValueBodySource = Bound(pair.First.Value, true),
            ConditionSource = pair.First.Condition is null ? null : Bound(pair.First.Condition, false)
        }).ToArray();
        var batch = command.Batch is null ? null : configured.Batch! with
        {
            ItemsSource = Bound(command.Batch.Items, false),
            EntityKeySource = ProjectionExpressionRenderer.RenderBody(Expand(command.Batch.EntityKey), ["entity"]),
            ItemKeySource = Item(command.Batch.ItemKey, false, "entity"),
            Updates = command.Batch.Updates.Zip(configured.Batch!.Updates).Select(pair => pair.Second with
            {
                ValueBodySource = Item(pair.First.Value, true, "entity"),
                ConditionSource = pair.First.Condition is null ? null : Item(pair.First.Condition, false, "entity")
            }).ToArray(),
            ExpectedVersions = command.Batch.ExpectedVersions.Zip(configured.Batch.ExpectedVersions)
                .Select(pair => pair.Second with { ValueSource = Item(pair.First.Value, true, "entity") }).ToArray()
        };
        var collections = command.Collections.Zip(configured.Collections ?? []).Select(pair => pair.Second with
        {
            ItemsSource = Bound(pair.First.Items, false),
            // Matching/key selectors retain production structural validation and null-component guards.
            ItemKeySource = pair.Second.MatchingKeyProperties is null ? Item(pair.First.ItemKey, false, "child")
                : "global::RonSijm.RepoGen.Abstractions.RepositoryCollectionKeys.Require(" + Item(pair.First.ItemKey, false, "child") + ")",
            ConditionSource = pair.First.Condition is null ? null : Bound(pair.First.Condition, false),
            Updates = pair.First.Updates.Zip(pair.Second.Updates).Select(update => update.Second with
            {
                ValueBodySource = Item(update.First.Value, true, "child"),
                ConditionSource = update.First.Condition is null ? null : Item(update.First.Condition, false, "child")
            }).ToArray(),
            ExpectedVersions = pair.First.ExpectedVersions.Zip(pair.Second.ExpectedVersions)
                .Select(version => version.Second with { ValueSource = Item(version.First.Value, true, "child") }).ToArray(),
            VisibilityPredicates = EntityForCollection(pair.First).VisibilityPolicies.Select(policy => ProjectionExpressionRenderer.RenderBody(Expand(policy.Predicate), ["child"])).ToArray()
        }).ToArray();
        // The collection fixture state already carries translated create expressions. Recover the child
        // declaration via the declared child key parameter, not by evaluating a graph or inspecting store rows.
        RepositoryEntityDesign EntityForCollection(RepositoryCollectionReconciliationDesign collection)
            => fullDesign.Entities.Single(definition => definition.EntityType == collection.ChildKey.Parameters[0].Type);
        var rendering = configured with
        {
            Predicates = predicates.ToArray(),
            Updates = updates,
            CreateSource = command.CreateMapping is null ? configured.CreateSource : Bound(command.CreateMapping, false),
            ResultSource = command.ResultProjection is null ? null : RenderBoundExpression(Expand(command.ResultProjection), parameters,
                skipRow: true, lambda: false, resultNullability: command.ResultNullability),
            ExpectedVersions = command.ExpectedVersions.Zip(configured.ExpectedVersions ?? []).Select(pair => pair.Second with { ValueSource = Bound(pair.First.Value, true) }).ToArray(),
            ContextKeySource = command.ContextKey is null ? null : Bound(command.ContextKey, false),
            Validations = command.Validations.Zip(configured.Validations ?? []).Select(pair => pair.Second with { PredicateSource = Bound(pair.First.Predicate, false) }).ToArray(),
            Batch = batch,
            Collections = collections.Length == 0 ? null : collections,
            CreateMany = command.CreateMany is null ? null : configured.CreateMany! with
            {
                ItemsSource = Bound(command.CreateMany.Items, false),
                CreateSource = ProjectionExpressionRenderer.RenderBody(Expand(command.CreateMany.Mapping), ["item"])
            }
        };
        return new(rendering, visibility);
    }
}
