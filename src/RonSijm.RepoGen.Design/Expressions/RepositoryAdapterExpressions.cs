using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Design;

/// <summary>Replaces only registered adapter markers. Never compiles expressions or invokes user getters/delegates.</summary>
internal sealed class RepositoryAdapterExpressions : ExpressionVisitor
{
    private readonly HashSet<RepositoryTypeAdapterDesign> registered;
    private readonly HashSet<RepositoryTypeAdapterDesign> expanding = new();

    internal RepositoryAdapterExpressions(IEnumerable<RepositoryTypeAdapterDesign> registered)
        => this.registered = new HashSet<RepositoryTypeAdapterDesign>(registered);

    internal LambdaExpression Expand(LambdaExpression expression) => (LambdaExpression)Visit(expression)!;

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType is not { IsGenericType: true } declaring ||
            declaring.GetGenericTypeDefinition() != typeof(RepositoryTypeAdapter<,>))
        {
            return base.VisitMethodCall(node);
        }

        if (ReadField(node.Object) is not IRepositoryTypeAdapter adapter)
        {
            throw new InvalidOperationException("RGD1701: Capture a registered type adapter in a local variable or field. Adapter properties, factories, and dynamic instances are not evaluated during generation.");
        }

        if (!registered.Contains(adapter.Design))
        {
            throw new InvalidOperationException("RGD1702: The type adapter was not registered on this RepositoryModelBuilder. Register it in this designer or a module applied to this builder.");
        }

        // Expand the argument before entering the mapping: nested calls with the same adapter
        // (for example ToContract(ToModel(id))) are not a recursive mapping definition.
        var argument = Visit(node.Arguments[0])!;
        if (!expanding.Add(adapter.Design))
        {
            throw new InvalidOperationException($"RGD1703: Recursive type adapter mapping '{adapter.Design.ModelType.FullName}' / '{adapter.Design.ContractType.FullName}'. Use an acyclic mapping.");
        }

        try
        {
            var mapping = node.Method.Name == nameof(RepositoryTypeAdapter<int, int>.ToModel)
                ? adapter.Design.ToModel
                : adapter.Design.ToContract;
            return Visit(new ReplaceParameter(mapping.Parameters[0], argument).Visit(mapping.Body)!)!;
        }
        finally { expanding.Remove(adapter.Design); }
    }

    private static object? ReadField(Expression? expression) => expression switch
    {
        ConstantExpression constant => constant.Value,
        UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast => ReadField(cast.Operand),
        MemberExpression { Member: FieldInfo field, Expression: not null } member when ReadField(member.Expression) is { } target => field.GetValue(target),
        _ => null
    };

    private sealed class ReplaceParameter : ExpressionVisitor
    {
        private readonly ParameterExpression parameter;
        private readonly Expression value;

        internal ReplaceParameter(ParameterExpression parameter, Expression value)
        {
            this.parameter = parameter;
            this.value = value;
        }

        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? value : node;
    }

    internal void Normalize(RepositoryEntityDesign entity, bool reads, bool writes)
    {
        if (entity.InMemoryStore is { } store)
        {
            if (store.EntityMapping is not null) store.EntityMapping = Expand(store.EntityMapping);
            if (store.WriteMapping is not null) store.WriteMapping = Expand(store.WriteMapping);
            foreach (var generated in store.GeneratedValues) generated.Value = Expand(generated.Value);
        }

        foreach (var accessor in entity.Accessors)
        {
            if (accessor.InputAdapter is null)
            {
                continue;
            }

            if (!registered.Contains(accessor.InputAdapter))
            {
                throw new InvalidOperationException("RGD1702: An accessor uses a type adapter registered on another RepositoryModelBuilder.");
            }

            accessor.InputMapping = Expand(accessor.InputAdapter.ToModel);
        }

        foreach (var filter in entity.Filters)
        {
            filter.Predicate = Expand(filter.Predicate);
        }

        foreach (var sort in entity.DefaultSorts)
        {
            sort.KeySelector = Expand(sort.KeySelector);
        }

        foreach (var policy in entity.VisibilityPolicies)
        {
            policy.Predicate = Expand(policy.Predicate);
        }

        foreach (var fragment in entity.Fragments)
        {
            fragment.Predicate = Expand(fragment.Predicate);
        }

        NormalizeIncludes(entity.Includes);
        foreach (var profile in entity.LoadProfiles)
        {
            NormalizeIncludes(profile.Includes);
        }

        foreach (var projection in entity.Projections)
        {
            NormalizeProjection(projection);
        }

        // Command ports may also declare reads; normalize only the selected capability's operations.
        if (reads || writes)
        {
            foreach (var query in entity.Queries.Where(query => query.Target == RepositoryQueryTarget.Write ? writes : reads))
            {
                NormalizeList(query.PredicatesList);
                foreach (var membership in query.Memberships)
                {
                    NormalizeMembership(membership);
                }

                NormalizeIncludes(query.Includes);
                foreach (var sort in query.Sorts)
                {
                    sort.KeySelector = Expand(sort.KeySelector);
                }

                foreach (var predicate in query.ConditionalPredicates)
                {
                    predicate.Condition = Expand(predicate.Condition);
                    predicate.Predicate = Expand(predicate.Predicate);
                }

                if (query.Projection is not null)
                {
                    NormalizeProjection(query.Projection);
                }

                if (query.Source is not null)
                {
                    query.Source.Expression = Expand(query.Source.Expression);
                }

                if (query.RegisteredSource is not null)
                {
                    query.RegisteredSource.RequestBinding = Expand(query.RegisteredSource.RequestBinding);
                }

                if (query.ContextKey is not null)
                {
                    query.ContextKey = Expand(query.ContextKey);
                }

                foreach (var validation in query.Validations)
                {
                    validation.Predicate = Expand(validation.Predicate);
                }

                foreach (var validation in query.CursorValidations)
                {
                    validation.Predicate = Expand(validation.Predicate);
                }

                if (query.Group is not null)
                {
                    query.Group.KeySelector = Expand(query.Group.KeySelector);
                    query.Group.Projection = Expand(query.Group.Projection);
                }

                if (query.Cursor is not null)
                {
                    query.Cursor.SeekPredicate = Expand(query.Cursor.SeekPredicate);
                    query.Cursor.CursorSelector = Expand(query.Cursor.CursorSelector);
                }

                foreach (var stage in query.ResultStages)
                {
                    if (stage.Expression is not null)
                    {
                        stage.Expression = Expand(stage.Expression);
                    }

                    if (stage.Condition is not null)
                    {
                        stage.Condition = Expand(stage.Condition);
                    }

                    if (stage.Projection is not null)
                    {
                        stage.Projection = Expand(stage.Projection);
                    }

                    if (stage.Membership is not null)
                    {
                        NormalizeMembership(stage.Membership);
                    }

                    if (stage.FieldSort is { } fieldSort)
                    {
                        fieldSort.FieldName = Expand(fieldSort.FieldName);
                        fieldSort.Descending = Expand(fieldSort.Descending);
                        foreach (var field in fieldSort.Fields)
                        {
                            field.KeySelector = Expand(field.KeySelector);
                        }
                    }
                }

                if (query.Dictionary is not null)
                {
                    query.Dictionary.KeySelector = Expand(query.Dictionary.KeySelector);
                    query.Dictionary.ValueSelector = Expand(query.Dictionary.ValueSelector);
                }

                if (query.Scalar is not null)
                {
                    query.Scalar.Selector = Expand(query.Scalar.Selector);
                }
            }
        }

        if (writes)
        {
            foreach (var command in entity.Commands)
            {
                NormalizeList(command.PredicatesList);
                NormalizeList(command.AllowedWritesList);
                if (command.CreateMapping is not null)
                {
                    command.CreateMapping = Expand(command.CreateMapping);
                }
                if (command.CreateMany is { } many)
                {
                    many.Items = Expand(many.Items);
                    many.Mapping = Expand(many.Mapping);
                }

                if (command.ResultProjection is not null)
                {
                    command.ResultProjection = Expand(command.ResultProjection);
                }

                if (command.ConflictKey is not null)
                {
                    command.ConflictKey = Expand(command.ConflictKey);
                }

                if (command.ContextKey is not null)
                {
                    command.ContextKey = Expand(command.ContextKey);
                }

                foreach (var validation in command.Validations)
                {
                    validation.Predicate = Expand(validation.Predicate);
                }

                foreach (var update in command.Updates)
                {
                    update.Property = Expand(update.Property);
                    update.Value = Expand(update.Value);
                    if (update.Condition is not null)
                    {
                        update.Condition = Expand(update.Condition);
                    }
                }

                foreach (var version in command.ExpectedVersions)
                {
                    version.Property = Expand(version.Property);
                    version.Value = Expand(version.Value);
                }

                if (command.Batch is { } batch)
                {
                    batch.Items = Expand(batch.Items);
                    batch.EntityKey = Expand(batch.EntityKey);
                    batch.ItemKey = Expand(batch.ItemKey);
                    foreach (var update in batch.Updates)
                    {
                        update.Property = Expand(update.Property);
                        update.Value = Expand(update.Value);
                        if (update.Condition is not null)
                        {
                            update.Condition = Expand(update.Condition);
                        }
                    }

                    foreach (var version in batch.ExpectedVersions)
                    {
                        version.Property = Expand(version.Property);
                        version.Value = Expand(version.Value);
                    }
                }
            }
        }
    }

    private void NormalizeProjection(RepositoryProjectionDesign projection)
    {
        projection.Expression = Expand(projection.Expression);
        NormalizeIncludes(projection.Includes);
    }

    private void NormalizeMembership(RepositoryMembershipDesign membership)
    {
        membership.KeySelector = Expand(membership.KeySelector);
        membership.Values = Expand(membership.Values);
    }

    private void NormalizeIncludes(IEnumerable<RepositoryIncludeDesign> includes)
    {
        foreach (var include in includes)
        {
            include.Navigation = Expand(include.Navigation);
            NormalizeList(include.ThenIncludesList);
        }
    }

    private void NormalizeList(List<LambdaExpression> expressions)
    {
        for (var index = 0; index < expressions.Count; index++)
        {
            expressions[index] = Expand(expressions[index]);
        }
    }
}
