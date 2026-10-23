using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Reviews inspectable navigation access. Never rewrites expressions or claims authorization enforcement.</summary>
internal static class RepositoryNavigationVisibilityValidator
{
    internal static IReadOnlyList<NavigationVisibilityOperationModel>? Create(IModel efModel, RepositoryDesign? design, RepositoryGenerationModel model)
    {
        if (design is null)
        {
            return null;
        }

        var operations = new List<NavigationVisibilityOperationModel>();
        var configured = model.Settings.RequireNavigationVisibilityReview;
        foreach (var generated in model.Entities)
        {
            if (model.Settings.CqrsSelectors && !generated.ReadEnabled && !generated.WriteEnabled)
            {
                continue;
            }

            var definition = design.Entities.FirstOrDefault(entity => RepositoryModelFactory.CSharpTypeName(entity.EntityType) == generated.EntityTypeName);
            if (definition is null)
            {
                continue;
            }

            var entityScope = new ReviewScope("entity", definition.NavigationVisibilityReviewRequired, definition.NavigationVisibilityReviews);
            ValidateScope(entityScope);
            var entityRequired = entityScope.Required ?? model.Settings.RequireNavigationVisibilityReview;
            if (model.Settings.ContractNamespace is null)
            {
                Record("selectorDefaults", "selectors", entityRequired, RepositoryExpressionInventory.Root(definition), [entityScope]);
                foreach (var filter in definition.Filters)
                {
                    Record(filter.MethodName, "filter", entityRequired, [filter.Predicate], [entityScope]);
                }

                foreach (var projection in definition.Projections)
                {
                    var scope = new ReviewScope("projection", projection.NavigationVisibilityReviewRequired, projection.NavigationVisibilityReviews);
                    ValidateScope(scope);
                    Record(projection.MethodName ?? "ProjectTo" + projection.ProjectionType.Name, "projection", scope.Required ?? entityRequired,
                        RepositoryExpressionInventory.Root(definition).Concat(RepositoryExpressionInventory.Loading(projection.Includes)).Append(projection.Expression), [scope, entityScope]);
                }
            }

            var activeQueries = definition.Queries.Where(query => model.Settings.ContractNamespace is null && !model.Settings.CqrsSelectors ||
                                                                  (query.Target == RepositoryQueryTarget.Write ? definition.WriteEnabled : definition.ReadEnabled)).ToArray();
            for (var index = 0; index < generated.Queries.Count; index++)
            {
                var query = activeQueries[index];
                var scope = new ReviewScope("query", query.NavigationVisibilityReviewRequired, query.NavigationVisibilityReviews);
                var projectionScope = new ReviewScope("projection", query.Projection?.NavigationVisibilityReviewRequired, query.Projection?.NavigationVisibilityReviews ?? []);
                ValidateScope(scope);
                ValidateScope(projectionScope);
                var expressions = RepositoryExpressionInventory.Query(query);
                if (query.Source is null && query.RegisteredSource is null)
                {
                    expressions = expressions.Concat(RepositoryExpressionInventory.Root(definition, query));
                }
                else if (query.Source is not null)
                {
                    expressions = expressions.Concat(RepositoryExpressionInventory.SourcePolicies(design, query));
                }

                Record(RepositoryOperationSignature.Query(generated.Queries[index]), "query", scope.Required ?? projectionScope.Required ?? entityRequired,
                    expressions, [scope, projectionScope, entityScope]);
            }

            for (var index = 0; index < generated.Commands.Count; index++)
            {
                var command = definition.Commands[index];
                var scope = new ReviewScope("command", command.NavigationVisibilityReviewRequired, command.NavigationVisibilityReviews);
                ValidateScope(scope);
                var expressions = RepositoryExpressionInventory.Command(command);
                if (command.Kind != RepositoryCommandKind.Create)
                {
                    expressions = expressions.Concat(definition.VisibilityPolicies.Where(policy => !command.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal))
                        .Select(static policy => policy.Predicate));
                }

                Record(RepositoryOperationSignature.Command(generated.Commands[index]), "command", scope.Required ?? entityRequired, expressions, [scope, entityScope]);
            }

            void Record(string operation, string kind, bool required, IEnumerable<Expression> expressions, ReviewScope[] scopes)
            {
                if (!required && !model.Settings.RequireNavigationVisibilityReview && scopes.All(static scope => scope.Required is null && scope.Reviews.Count == 0))
                {
                    return;
                }

                configured = true;
                var accesses = expressions.SelectMany(expression => RepositoryQuerySourceRenderer.NavigationReferences(efModel, expression)).Distinct()
                    .SelectMany(navigation => (design.Entities.FirstOrDefault(entity => entity.EntityType == navigation.TargetEntityType.ClrType)?.VisibilityPolicies ?? [])
                        .Select(policy => Access(navigation, policy.Name)))
                    .OrderBy(static access => access.DeclaringEntity, StringComparer.Ordinal).ThenBy(static access => access.Navigation, StringComparer.Ordinal)
                    .ThenBy(static access => access.Policy, StringComparer.Ordinal).ToArray();
                operations.Add(new NavigationVisibilityOperationModel(generated.EntityTypeName, operation, kind, required, accesses));

                NavigationVisibilityAccessModel Access(INavigationBase navigation, string policy)
                {
                    var found = scopes.SelectMany(scope => scope.Reviews.Select(review => (Scope: scope.Name, Review: review)))
                        .FirstOrDefault(value => value.Review.EntityType == navigation.TargetEntityType.ClrType && value.Review.PolicyName == policy);
                    if (required && found.Review is null)
                    {
                        throw new InvalidOperationException($"RGD2001: '{generated.EntityTypeName}.{operation}' traverses navigation '{navigation.DeclaringEntityType.ClrType.FullName}.{navigation.Name}' " +
                                                            $"to policy-protected '{navigation.TargetEntityType.ClrType.FullName}' ({policy}). RepoGen root policies do not filter navigations. " +
                                                            "Use explicit relationship predicates/loading, an EF global filter, or a reviewed alternative; record ReviewNavigationVisibility<TTarget>(policyName, reason) in the designer.");
                    }

                    return new NavigationVisibilityAccessModel(RepositoryModelFactory.CSharpTypeName(navigation.DeclaringEntityType.ClrType), navigation.Name,
                        RepositoryModelFactory.CSharpTypeName(navigation.TargetEntityType.ClrType), policy, found.Review?.Reason, found.Review is null ? null : found.Scope);
                }
            }
        }

        return configured
            ? operations.OrderBy(static operation => operation.Entity, StringComparer.Ordinal)
                .ThenBy(static operation => operation.Kind, StringComparer.Ordinal).ThenBy(static operation => operation.Operation, StringComparer.Ordinal).ToArray()
            : null;

        void ValidateScope(ReviewScope scope)
        {
            foreach (var review in scope.Reviews)
            {
                var target = efModel.FindEntityType(review.EntityType);
                if (target is null || !(design.Entities.FirstOrDefault(entity => entity.EntityType == review.EntityType)?.VisibilityPolicies ?? [])
                        .Any(policy => policy.Name == review.PolicyName))
                {
                    throw new InvalidOperationException($"RGD2002: Navigation visibility review references unknown mapped target/policy '{review.EntityType.FullName}.{review.PolicyName}'. Policy names are case-sensitive.");
                }
            }
        }
    }

    private sealed record ReviewScope(string Name, bool? Required, IReadOnlyList<RepositoryNavigationVisibilityReview> Reviews);
}