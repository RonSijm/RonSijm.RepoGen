using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Validates typed roots and rewrites them to one captured operation context, never invoking designer code.</summary>
internal static class RepositoryQuerySourceRenderer
{
    public static ConfiguredQuerySourceModel? Create(IEntityType owner, RepositoryQueryDesign query,
        List<FilterParameterModel> parameters, RepositoryTrackingBehavior tracking, RepositoryDesign? design = null)
    {
        if (query.Source is null)
        {
            return null;
        }

        if (query.Predicates.Count != 0 || query.Memberships.Count != 0 || query.ConditionalPredicates.Count != 0 || query.Includes.Count != 0 ||
            query.Sorts.Count != 0 || query.DynamicSort is not null || query.Projection is not null || query.Group is not null || query.Cursor is { OnResults: false })
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' uses From(). Entity-level clauses cannot be combined with a replacement root; use the From expression or its result pipeline.");
        }

        var factory = query.Source.Expression;
        var visitor = new SourceVisitor(owner.Model, factory.Parameters[0], query, tracking, design);
        var body = visitor.Visit(factory.Body)!;
        if (visitor.Roots.Count == 0)
        {
            throw new InvalidOperationException($"Query '{query.MethodName}' From() must use at least one RepositoryQuerySources.Set<TEntity>() root.");
        }

        var expression = Expression.Lambda(body, factory.Parameters);
        return new ConfiguredQuerySourceModel(
            RepositoryModelFactory.RenderBoundExpression(expression, parameters, skipRow: true, lambda: false, substitutions: visitor.Roots,
                resultNullability: query.Source.ResultType == query.ResultNullability?.Type ? query.ResultNullability : null),
            visitor.Entities.Select(static type => RepositoryModelFactory.CSharpTypeName(type)).Order(StringComparer.Ordinal).ToArray(),
            visitor.Operators.Order(StringComparer.Ordinal).ToArray());
    }

    internal static IReadOnlyCollection<Type> NavigationDependencies(IModel model, Expression expression)
    {
        var visitor = new NavigationVisitor(model);
        visitor.Visit(expression);
        return visitor.Entities;
    }

    internal static IReadOnlyCollection<INavigationBase> NavigationReferences(IModel model, Expression expression)
    {
        var visitor = new NavigationVisitor(model);
        visitor.Visit(expression);
        return visitor.Navigations;
    }

    internal static IReadOnlyCollection<Type> QueryFilterDependencies(IModel model, IEnumerable<string> participatingTypeNames)
    {
        var names = participatingTypeNames.ToHashSet(StringComparer.Ordinal);
        var visited = new HashSet<IEntityType>();
        var dependencies = new HashSet<Type>();
        while (model.GetEntityTypes().FirstOrDefault(entity => !visited.Contains(entity) && names.Contains(RepositoryModelFactory.CSharpTypeName(entity.ClrType))) is { } entity)
        {
            visited.Add(entity);
            foreach (var filter in entity.GetAllBaseTypes().Append(entity).Select(static definition => definition.GetQueryFilter()).OfType<LambdaExpression>())
            {
                var visitor = new FilterDependencyVisitor(model);
                visitor.Visit(filter);
                foreach (var type in visitor.Entities)
                {
                    dependencies.Add(type);
                    names.Add(RepositoryModelFactory.CSharpTypeName(type));
                }
            }
        }

        return dependencies;
    }

    private class NavigationVisitor(IModel model) : ExpressionVisitor
    {
        protected IModel Model { get; } = model;
        public HashSet<Type> Entities { get; } = [];
        public HashSet<INavigationBase> Navigations { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is not null && Model.FindEntityType(node.Expression.Type) is { } entity)
            {
                AddNavigation(entity, node.Member.Name);
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Property) &&
                node.Arguments is [var row, ConstantExpression { Value: string name }])
            {
                while (row is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast)
                {
                    row = cast.Operand;
                }

                if (Model.FindEntityType(row.Type) is { } entity)
                {
                    AddNavigation(entity, name);
                }
            }

            return base.VisitMethodCall(node);
        }

        private void AddNavigation(IEntityType entity, string name)
        {
            var navigation = (INavigationBase?)entity.FindNavigation(name) ?? entity.FindSkipNavigation(name);
            if (navigation is null)
            {
                return;
            }

            Navigations.Add(navigation);
            Entities.Add(navigation.TargetEntityType.ClrType);
        }
    }

    private sealed class SourceVisitor(
        IModel model,
        ParameterExpression sources,
        RepositoryQueryDesign query,
        RepositoryTrackingBehavior tracking,
        RepositoryDesign? design) : NavigationVisitor(model)
    {
        public HashSet<string> Operators { get; } = new(StringComparer.Ordinal);
        public Dictionary<ParameterExpression, string> Roots { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(RepositoryQuerySources) && node.Method.Name == nameof(RepositoryQuerySources.Set))
            {
                if (node.Object != sources)
                {
                    throw Invalid("Set() must be called on the expression's sources parameter");
                }

                var type = node.Method.GetGenericArguments()[0];
                var entity = Model.FindEntityType(type);
                if (entity is null || entity.IsOwned() || entity.HasSharedClrType)
                {
                    throw Invalid($"source '{type.FullName}' is not a supported non-owned, non-shared entity in this DbContext's EF model");
                }

                Entities.Add(type);
                var root = "__repoGenContext.Set<" + RepositoryModelFactory.CSharpTypeName(type) + ">()";
                foreach (var policy in design?.Entities.FirstOrDefault(definition => definition.EntityType == type)?.VisibilityPolicies ?? [])
                {
                    if (!query.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal))
                    {
                        root = "global::System.Linq.Queryable.Where(" + root + ", " + ProjectionExpressionRenderer.Render(policy.Predicate) + ")";
                    }
                }

                var trackingMethod = tracking switch
                {
                    RepositoryTrackingBehavior.Tracking => nameof(EntityFrameworkQueryableExtensions.AsTracking),
                    RepositoryTrackingBehavior.NoTracking => nameof(EntityFrameworkQueryableExtensions.AsNoTracking),
                    RepositoryTrackingBehavior.NoTrackingWithIdentityResolution => nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution),
                    _ => null
                };
                if (trackingMethod is not null)
                {
                    root = Policy("EntityFrameworkQueryableExtensions", trackingMethod, root);
                }

                if (query.IgnoreQueryFilters)
                {
                    root = Policy("EntityFrameworkQueryableExtensions", nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters), root);
                }

                if (query.IgnoreAutoIncludes)
                {
                    root = Policy("EntityFrameworkQueryableExtensions", nameof(EntityFrameworkQueryableExtensions.IgnoreAutoIncludes), root);
                }

                if (query.SplittingBehavior != RepositoryQuerySplittingBehavior.Unspecified)
                {
                    root = Policy("RelationalQueryableExtensions", query.SplittingBehavior == RepositoryQuerySplittingBehavior.SplitQuery
                        ? "AsSplitQuery"
                        : "AsSingleQuery", root);
                }

                if (query.QueryTag is not null)
                {
                    root = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.TagWith(" + root + ", " +
                           ProjectionExpressionRenderer.RenderBody(Expression.Lambda(Expression.Constant(query.QueryTag)), []) + ")";
                }

                var placeholder = Expression.Parameter(node.Type, "__repoGenSource" + Roots.Count);
                Roots.Add(placeholder, root);
                return placeholder;
            }

            if (node.Method.Name is "AsEnumerable" or "AsAsyncEnumerable" or "AsQueryable" or "ToList" or "ToArray" or "ToDictionary" or "ToLookup" or "ToHashSet" or "Compile")
            {
                throw Invalid($"'{node.Method.Name}' is a materialization/client-query boundary; keep From() queryable and choose a generated terminal instead");
            }

            if (typeof(IQueryable).IsAssignableFrom(node.Type) && node.Method.DeclaringType != typeof(Queryable))
            {
                throw Invalid($"query-producing method '{node.Method.Name}' is opaque; use typed Set() roots and standard Queryable operators");
            }

            if (node.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(node.Type) &&
                node.Method.DeclaringType != typeof(Queryable) && node.Method.DeclaringType != typeof(Enumerable) && !MappedBinaryRead(node))
            {
                throw Invalid($"sequence-producing method '{node.Method.Name}' is opaque and cannot declare a query source");
            }

            if (node.Method.DeclaringType == typeof(Queryable) || node.Method.DeclaringType == typeof(Enumerable))
            {
                Operators.Add(node.Method.Name);
            }

            return base.VisitMethodCall(node);
        }

        private bool MappedBinaryRead(MethodCallExpression node)
        {
            // A model byte[] scalar is not an opaque query root just because it implements IEnumerable.
            if (node.Type != typeof(byte[]))
            {
                return false;
            }
            if (node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Property) &&
                node.Arguments is [var receiver, ConstantExpression { Value: string name }])
            {
                return Model.FindEntityType(receiver.Type)?.FindProperty(name)?.ClrType == typeof(byte[]);
            }
            return node is { Object: { } instance, Arguments: [ConstantExpression { Value: string key }] } &&
                Model.FindEntityType(instance.Type)?.FindProperty(key) is { ClrType: var type } property && type == typeof(byte[]) &&
                property.IsIndexerProperty() && property.PropertyInfo?.GetMethod == node.Method;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (typeof(IQueryable).IsAssignableFrom(node.Type))
            {
                throw Invalid("queryable members are not typed Set() roots");
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (typeof(IQueryable).IsAssignableFrom(node.Type))
            {
                throw Invalid("runtime IQueryable inputs are not allowed; pass an application request, not a query source");
            }

            return base.VisitParameter(node);
        }

        private InvalidOperationException Invalid(string message) => new($"Query '{query.MethodName}' From(): {message}.");

        private static string Policy(string extensions, string methodName, string root) =>
            "global::Microsoft.EntityFrameworkCore." + extensions + "." + methodName + "(" + root + ")";
    }

    private sealed class FilterDependencyVisitor(IModel model) : NavigationVisitor(model)
    {
        protected override Expression VisitExtension(Expression node)
        {
            // EF normalizes DbSet/Set references to entity roots during model finalization.
            if (node is Microsoft.EntityFrameworkCore.Query.EntityQueryRootExpression root)
            {
                Entities.Add(root.EntityType.ClrType);
            }

            return base.VisitExtension(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(DbContext) && node.Method.Name == nameof(DbContext.Set) && node.Method.IsGenericMethod)
            {
                AddMappedType(node.Method.GetGenericArguments()[0]);
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            // Inspect the declared queryable element, not the value of a captured DbContext/property.
            foreach (var contract in node.Type.GetInterfaces().Append(node.Type))
            {
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IQueryable<>))
                {
                    AddMappedType(contract.GetGenericArguments()[0]);
                }
            }

            return base.VisitMember(node);
        }

        private void AddMappedType(Type type)
        {
            if (Model.GetEntityTypes().Any(entity => entity.ClrType == type))
            {
                Entities.Add(type);
            }
        }
    }
}
