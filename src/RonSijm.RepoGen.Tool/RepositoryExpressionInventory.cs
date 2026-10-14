using System.Linq.Expressions;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Shared inspectable expression inventory for visibility review and cache dependencies.</summary>
internal static class RepositoryExpressionInventory
{
    internal static IEnumerable<Expression> Loading(IEnumerable<RepositoryIncludeDesign> includes) =>
        includes.SelectMany(static include => new[] { include.Navigation }.Concat(include.ThenIncludes));

    internal static IEnumerable<Expression> Query(RepositoryQueryDesign query, bool includeLoading = true)
    {
        foreach (var expression in query.Predicates)
        {
            yield return expression;
        }

        foreach (var membership in query.Memberships)
        {
            yield return membership.KeySelector;
            yield return membership.Values;
        }

        foreach (var predicate in query.ConditionalPredicates)
        {
            yield return predicate.Condition;
            yield return predicate.Predicate;
        }

        foreach (var sort in query.Sorts)
        {
            yield return sort.KeySelector;
        }

        foreach (var expression in includeLoading ? Loading(query.Includes) : [])
        {
            yield return expression;
        }

        if (query.Projection is { } projection)
        {
            yield return projection.Expression;
            foreach (var expression in includeLoading ? Loading(projection.Includes) : [])
            {
                yield return expression;
            }
        }

        if (query.Source is { } source)
        {
            yield return source.Expression;
        }

        if (query.Group is { } group)
        {
            yield return group.KeySelector;
            yield return group.Projection;
        }

        if (query.Cursor is { } cursor)
        {
            yield return cursor.SeekPredicate;
            yield return cursor.CursorSelector;
        }

        foreach (var validation in query.CursorValidations)
        {
            yield return validation.Predicate;
        }

        if (query.Dictionary is { } dictionary)
        {
            yield return dictionary.KeySelector;
            yield return dictionary.ValueSelector;
        }

        if (query.Scalar is { } scalar)
        {
            yield return scalar.Selector;
        }

        foreach (var stage in query.ResultStages)
        {
            if (stage.Expression is not null)
            {
                yield return stage.Expression;
            }

            if (stage.Condition is not null)
            {
                yield return stage.Condition;
            }

            if (stage.Projection is not null)
            {
                yield return stage.Projection;
            }

            if (stage.Membership is { } membership)
            {
                yield return membership.KeySelector;
                yield return membership.Values;
            }

            if (stage.FieldSort is { } fieldSort)
            {
                yield return fieldSort.FieldName;
                yield return fieldSort.Descending;
                foreach (var field in fieldSort.Fields)
                {
                    yield return field.KeySelector;
                }
            }
        }
    }

    internal static IEnumerable<Expression> Root(RepositoryEntityDesign entity, RepositoryQueryDesign? query = null, bool includeLoading = true)
    {
        foreach (var policy in entity.VisibilityPolicies)
        {
            if (query?.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal) != true)
            {
                yield return policy.Predicate;
            }
        }

        foreach (var expression in includeLoading ? Loading(entity.Includes) : [])
        {
            yield return expression;
        }

        if (query?.UseDefaultSort != false)
        {
            foreach (var sort in entity.DefaultSorts)
            {
                yield return sort.KeySelector;
            }
        }
    }

    internal static IEnumerable<Expression> SourcePolicies(RepositoryDesign? design, RepositoryQueryDesign query)
    {
        if (query.Source is null || design is null)
        {
            return [];
        }

        var roots = new SourceRoots();
        roots.Visit(query.Source.Expression);
        return design.Entities.Where(entity => roots.Types.Contains(entity.EntityType))
            .SelectMany(entity => entity.VisibilityPolicies.Where(policy => !query.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal)))
            .Select(static policy => policy.Predicate);
    }

    private sealed class SourceRoots : ExpressionVisitor
    {
        internal HashSet<Type> Types { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(RepositoryQuerySources) && node.Method.Name == nameof(RepositoryQuerySources.Set))
            {
                Types.Add(node.Method.GetGenericArguments()[0]);
            }

            return base.VisitMethodCall(node);
        }
    }

    internal static IEnumerable<Expression> Command(RepositoryCommandDesign command, bool includeCollections = true, bool includeResult = true)
    {
        foreach (var expression in command.Predicates)
        {
            yield return expression;
        }

        if (command.CreateMapping is not null)
        {
            yield return command.CreateMapping;
        }
        if (command.CreateMany is { } many)
        {
            yield return many.Items;
            yield return many.Mapping;
        }

        if (includeResult && command.ResultProjection is not null)
        {
            yield return command.ResultProjection;
        }

        foreach (var update in command.Updates)
        {
            yield return update.Property;
            yield return update.Value;
            if (update.Condition is not null)
            {
                yield return update.Condition;
            }
        }

        foreach (var version in command.ExpectedVersions)
        {
            yield return version.Property;
            yield return version.Value;
        }

        if (command.Batch is { } batch)
        {
            yield return batch.Items;
            yield return batch.EntityKey;
            yield return batch.ItemKey;
            foreach (var update in batch.Updates)
            {
                yield return update.Property;
                yield return update.Value;
                if (update.Condition is not null) yield return update.Condition;
            }

            foreach (var version in batch.ExpectedVersions)
            {
                yield return version.Property;
                yield return version.Value;
            }
        }

        foreach (var collection in includeCollections ? command.Collections : [])
        {
            yield return collection.Navigation;
            yield return collection.Items;
            yield return collection.ChildKey;
            yield return collection.ItemKey;
            if (collection.CreateMapping is not null) yield return collection.CreateMapping;
            if (collection.Condition is not null) yield return collection.Condition;
            foreach (var update in collection.Updates)
            {
                yield return update.Property;
                yield return update.Value;
                if (update.Condition is not null) yield return update.Condition;
            }

            foreach (var version in collection.ExpectedVersions)
            {
                yield return version.Property;
                yield return version.Value;
            }
        }
    }
}
