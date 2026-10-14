using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Design;

#pragma warning disable CA1720 // Single mirrors LINQ's established cardinality terminology.

public sealed partial class RepositoryQueryBuilder<TEntity>
    where TEntity : class
{
    /// <summary>Overrides entity/model SQL-command timeouts for this named closed query. Null uses the context setting.</summary>
    public RepositoryQueryBuilder<TEntity> UseCommandTimeout(int? seconds)
    {
        design.CommandTimeout = new RepositoryCommandTimeoutDesign(seconds);
        return this;
    }

    /// <summary>Removes this named query's override and inherits the entity/model setting again.</summary>
    public RepositoryQueryBuilder<TEntity> InheritCommandTimeout()
    {
        design.CommandTimeout = null;
        return this;
    }

    /// <summary>Exposes this named read on the write repository instead of the read repository. Requires a CQRS or closed surface.</summary>
    /// <remarks>Uses the ordinary selector tracking policy unless explicitly overridden. Does not save, lock rows or authorize access.
    /// Pass false to restore the default read placement. Enabled command-side queries cannot be cached.</remarks>
    public RepositoryQueryBuilder<TEntity> OnWriteRepository(bool enabled = true)
    {
        design.Target = enabled ? RepositoryQueryTarget.Write : RepositoryQueryTarget.Read;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> RequirePagingOrder(bool enabled = true)
    {
        design.RequirePagingOrder = enabled;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> Paged(RepositoryPageConsistency consistency)
    {
        if (consistency is < RepositoryPageConsistency.BestEffort or > RepositoryPageConsistency.Serializable)
        {
            throw new ArgumentOutOfRangeException(nameof(consistency));
        }

        design.PageConsistency = consistency;
        return HasResult(RepositoryQueryResultKind.Paged);
    }

    /// <summary>Uses an explicitly registered infrastructure query provider, configured only here rather than supplied by callers.</summary>
    public RepositoryResultQueryBuilder<TResult> FromProvider<TProvider, TRequest, TResult>(Expression<Func<TRequest, TRequest>> request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (design.Source is not null || design.RegisteredSource is not null || design.ResultInputType is not null || design.Predicates.Count != 0 || design.Memberships.Count != 0 ||
            design.ConditionalPredicates.Count != 0 || design.Includes.Count != 0 || design.LoadProfileNames.Count != 0 || design.Sorts.Count != 0 ||
            design.DynamicSort is not null || design.Projection is not null || design.ProjectionTypeReference is not null || design.ProjectionMethodNameReference is not null || design.Group is not null || design.Cursor is not null)
        {
            throw new InvalidOperationException("FromProvider replaces the query root. Configure result stages after it, not entity clauses before it.");
        }

        design.RegisteredSource = new RepositoryRegisteredQueryDesign(typeof(TProvider), typeof(TRequest), typeof(TResult), request);
        design.UseDefaultSort = false;
        design.ResultInputType = typeof(TResult);
        return new RepositoryResultQueryBuilder<TResult>(design);
    }

    public RepositoryQueryBuilder<TEntity> Validate<TRequest>(Expression<Func<TRequest, bool>> predicate, string message)
    {
        design.ValidationsList.Add(new RepositoryInputValidationDesign(predicate, message));
        return this;
    }

    public RepositoryQueryBuilder<TEntity> AllowNullInput<TInput>(string parameterName) where TInput : class?
    {
        design.InputNullability = design.InputNullability.Concat(new[] { new RepositoryInputNullabilityDesign(parameterName, typeof(TInput)) }).ToArray();
        return this;
    }

    public RepositoryQueryBuilder<TEntity> InputNullability<TInput>(string parameterName, Action<RepositoryTypeNullabilityBuilder> configure)
    {
        design.InputNullability = design.InputNullability.Concat(new[]
        {
            new RepositoryInputNullabilityDesign(parameterName,
                RepositoryTypeNullabilityBuilder.Create(typeof(TInput), configure))
        }).ToArray();
        return this;
    }

    /// <summary>Annotates the final row, dictionary/lookup value, or aggregate value, not the Task/container.</summary>
    public RepositoryQueryBuilder<TEntity> ResultNullability<TValue>(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (design.ResultNullability is not null)
        {
            throw new InvalidOperationException($"RGN0005: Query '{design.MethodName}' configures result nullability more than once.");
        }

        design.ResultNullability = RepositoryTypeNullabilityBuilder.Create(typeof(TValue), configure);
        return this;
    }

    /// <summary>Annotates a dictionary/lookup key's nested references. The key itself must remain non-null.</summary>
    public RepositoryQueryBuilder<TEntity> KeyNullability<TKey>(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (design.DictionaryKeyNullability is not null)
        {
            throw new InvalidOperationException($"RGN0005: Query '{design.MethodName}' configures key nullability more than once.");
        }

        design.DictionaryKeyNullability = RepositoryTypeNullabilityBuilder.Create(typeof(TKey), configure);
        return this;
    }

    public RepositoryQueryBuilder<TEntity> Use<TRequest>(RepositoryQueryFragment<TEntity, TRequest> fragment)
    {
        design.PredicatesList.Add((fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate);
        return this;
    }

    /// <summary>Applies a fragment only when the request condition is true. Binds its request to the condition's parameter name.</summary>
    public RepositoryQueryBuilder<TEntity> UseIf<TRequest>(Expression<Func<TRequest, bool>> condition, RepositoryQueryFragment<TEntity, TRequest> fragment)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        return WhereIf(condition, (Expression<Func<TEntity, TRequest, bool>>)RepositoryFragmentExpressions.BindRequest(fragment.Design.Predicate, condition.Parameters[0]));
    }

    /// <summary>Conditionally applies a request-independent fragment. Inactive clauses are omitted from the query.</summary>
    public RepositoryQueryBuilder<TEntity> UseIf<TRequest>(Expression<Func<TRequest, bool>> condition, RepositoryQueryFragment<TEntity> fragment)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        return WhereIf(condition, (Expression<Func<TEntity, TRequest, bool>>)RepositoryFragmentExpressions.BindRequest(fragment.Design.Predicate, condition.Parameters[0]));
    }

    public RepositoryQueryBuilder<TEntity> UseContextKey<TRequest, TKey>(Expression<Func<TRequest, TKey>> key)
    {
        design.ContextKey = key ?? throw new ArgumentNullException(nameof(key));
        return this;
    }

    public RepositoryQueryBuilder<TEntity> BypassVisibility(params string[] names)
    {
        if (names is null)
        {
            throw new ArgumentNullException(nameof(names));
        }

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A visibility policy name is required.", nameof(names));
            }

            design.BypassedVisibilityList.Add(name.Trim());
        }

        return this;
    }

    private readonly RepositoryQueryDesign design;

    internal RepositoryQueryBuilder(RepositoryQueryDesign design)
    {
        this.design = design;
    }

    public RepositoryQueryBuilder<TEntity> RequireNavigationVisibilityReview(bool? enabled = true)
    {
        design.NavigationVisibility.Required = enabled;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> ReviewNavigationVisibility<TTarget>(string policyName, string reason) where TTarget : class
    {
        design.NavigationVisibility.Review<TTarget>(policyName, reason);
        return this;
    }

    /// <summary>Starts an inspectable query over typed sets; joins and subqueries stay in the generated IQueryable pipeline.</summary>
    public RepositoryResultQueryBuilder<TResult> From<TResult>(
        Expression<Func<RepositoryQuerySources, IQueryable<TResult>>> query) => StartSource<TResult>(query);

    /// <summary>Binds a request to the query root. The same named/typed parameter can be reused by result stages.</summary>
    public RepositoryResultQueryBuilder<TResult> From<TRequest, TResult>(
        Expression<Func<RepositoryQuerySources, TRequest, IQueryable<TResult>>> query) => StartSource<TResult>(query);

    private RepositoryResultQueryBuilder<TResult> StartSource<TResult>(LambdaExpression query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (design.Source is not null || design.RegisteredSource is not null || design.ResultInputType is not null)
        {
            throw new InvalidOperationException("From() must start a query before its result pipeline and may only be configured once.");
        }

        if (design.Predicates.Count != 0 || design.Memberships.Count != 0 || design.ConditionalPredicates.Count != 0 || design.Includes.Count != 0 || design.LoadProfileNames.Count != 0 ||
            design.Sorts.Count != 0 || design.DynamicSort is not null || design.Projection is not null ||
            design.ProjectionTypeReference is not null || design.ProjectionMethodNameReference is not null || design.Group is not null || design.Cursor is not null)
        {
            throw new InvalidOperationException("From() replaces the entity root. Put filters, sorting, and projection inside From() or its result pipeline, not before it.");
        }

        design.Source = new RepositoryQuerySourceDesign(query, typeof(TResult));
        design.UseDefaultSort = false;
        design.ResultInputType = typeof(TResult);
        return new RepositoryResultQueryBuilder<TResult>(design);
    }

    public RepositoryQueryBuilder<TEntity> Use(RepositoryQueryFragment<TEntity> fragment)
    {
        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        design.PredicatesList.Add(fragment.Design.Predicate);
        return this;
    }

    public RepositoryQueryBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate) =>
        AddPredicate(predicate);

    /// <summary>Filters by request-bound values. Empty inputs match nothing by default; null inputs are rejected.</summary>
    public RepositoryQueryBuilder<TEntity> WhereIn<TValue, TRequest>(Expression<Func<TEntity, TValue>> keySelector,
        Expression<Func<TRequest, IEnumerable<TValue>?>> values, Action<RepositoryMembershipOptionsBuilder>? configure = null)
    {
        RequireEntityMembership();
        design.MembershipsList.Add(RepositoryMembershipDesign.Create(keySelector, values, negated: false, configure));
        return this;
    }

    /// <summary>Excludes request-bound values. Empty inputs exclude nothing by default; null inputs are rejected.</summary>
    public RepositoryQueryBuilder<TEntity> WhereNotIn<TValue, TRequest>(Expression<Func<TEntity, TValue>> keySelector,
        Expression<Func<TRequest, IEnumerable<TValue>?>> values, Action<RepositoryMembershipOptionsBuilder>? configure = null)
    {
        RequireEntityMembership();
        design.MembershipsList.Add(RepositoryMembershipDesign.Create(keySelector, values, negated: true, configure));
        return this;
    }

    private void RequireEntityMembership()
    {
        if (design.Source is not null || design.RegisteredSource is not null || design.ResultInputType is not null)
        {
            throw new InvalidOperationException("RGD1902: Configure row membership before Results(), or on the current From()/Select()/Results() result builder.");
        }
    }

    public RepositoryQueryBuilder<TEntity> Where<TParameter>(
        Expression<Func<TEntity, TParameter, bool>> predicate) => AddPredicate(predicate);

    public RepositoryQueryBuilder<TEntity> Where<TParameter1, TParameter2>(
        Expression<Func<TEntity, TParameter1, TParameter2, bool>> predicate) => AddPredicate(predicate);

    public RepositoryQueryBuilder<TEntity> Where<TParameter1, TParameter2, TParameter3>(
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, bool>> predicate) => AddPredicate(predicate);

    public RepositoryQueryBuilder<TEntity> Where<TParameter1, TParameter2, TParameter3, TParameter4>(
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TParameter4, bool>> predicate) =>
        AddPredicate(predicate);

    public RepositoryQueryBuilder<TEntity> WhereIf<TParameter>(
        Expression<Func<TParameter, bool>> condition,
        Expression<Func<TEntity, TParameter, bool>> predicate)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        design.ConditionalPredicatesList.Add(new RepositoryConditionalPredicateDesign(condition, predicate));
        return this;
    }

    public RepositoryQueryBuilder<TEntity> Include<TNavigation>(
        Expression<Func<TEntity, TNavigation>> navigation)
    {
        if (navigation is null)
        {
            throw new ArgumentNullException(nameof(navigation));
        }

        design.IncludesList.Add(new RepositoryIncludeDesign(navigation));
        return this;
    }

    public RepositoryQueryBuilder<TEntity> Load(Action<RepositoryLoadBuilder<TEntity>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(new RepositoryLoadBuilder<TEntity>(design.IncludesList));
        return this;
    }

    public RepositoryQueryBuilder<TEntity> Load<TRequest>(Action<RepositoryLoadBuilder<TEntity, TRequest>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(new RepositoryLoadBuilder<TEntity, TRequest>(design.IncludesList));
        return this;
    }

    public RepositoryQueryBuilder<TEntity> UseLoadProfile(string name)
    {
        design.Loading.Use(name);
        return this;
    }

    public RepositoryQueryBuilder<TEntity> UseLoadProfile(RepositoryLoadProfile<TEntity> profile)
    {
        design.Loading.Use(profile);
        return this;
    }

    /// <summary>Opt-in validation requiring AsSingleQuery/AsSplitQuery for multiple collection paths.</summary>
    public RepositoryQueryBuilder<TEntity> RequireExplicitLoadStrategy(bool required = true)
    {
        design.RequireExplicitLoadStrategy = required;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        AddSort(keySelector, descending: false);

    public RepositoryQueryBuilder<TEntity> OrderBy<TKey, TParameter>(Expression<Func<TEntity, TParameter, TKey>> keySelector) =>
        AddSort(keySelector, descending: false);

    public RepositoryQueryBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        AddSort(keySelector, descending: true);

    public RepositoryQueryBuilder<TEntity> OrderByDescending<TKey, TParameter>(Expression<Func<TEntity, TParameter, TKey>> keySelector) =>
        AddSort(keySelector, descending: true);

    public RepositoryQueryBuilder<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        AddSort(keySelector, descending: false);

    public RepositoryQueryBuilder<TEntity> ThenBy<TKey, TParameter>(Expression<Func<TEntity, TParameter, TKey>> keySelector) =>
        AddSort(keySelector, descending: false);

    public RepositoryQueryBuilder<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        AddSort(keySelector, descending: true);

    public RepositoryQueryBuilder<TEntity> ThenByDescending<TKey, TParameter>(Expression<Func<TEntity, TParameter, TKey>> keySelector) =>
        AddSort(keySelector, descending: true);

    public RepositoryQueryBuilder<TEntity> AllowSorting(
        params Expression<Func<TEntity, object?>>[] properties) =>
        AllowSorting("sortBy", "descending", properties);

    public RepositoryQueryBuilder<TEntity> AllowSorting(
        string fieldNameParameter,
        string descendingParameter,
        params Expression<Func<TEntity, object?>>[] properties)
    {
        if (properties is null || properties.Length == 0)
        {
            throw new ArgumentException("At least one sortable property is required.", nameof(properties));
        }

        var fields = properties.Select(GetDirectPropertyName).ToArray();
        design.DynamicSort = new RepositoryDynamicSortDesign(
            RequireParameterName(fieldNameParameter),
            RequireParameterName(descendingParameter),
            fields);
        return this;
    }

    public RepositoryQueryBuilder<TEntity> ProjectTo<TProjection>(
        Expression<Func<TEntity, TProjection>> projection)
    {
        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        design.Projection = new RepositoryProjectionDesign(typeof(TProjection), projection);
        design.ProjectionTypeReference = null;
        design.ProjectionMethodNameReference = null;
        design.Group = null;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> ProjectTo<TProjection>()
    {
        design.Projection = null;
        design.ProjectionTypeReference = typeof(TProjection);
        design.ProjectionMethodNameReference = null;
        design.Group = null;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> ProjectTo(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("A projection method name is required.", nameof(methodName));
        }

        design.Projection = null;
        design.ProjectionTypeReference = null;
        design.ProjectionMethodNameReference = methodName.Trim();
        design.Group = null;
        return this;
    }

    /// <summary>Starts clauses over the configured projection or grouped result, not the entity.</summary>
    public RepositoryResultQueryBuilder<TResult> Results<TResult>()
    {
        if (design.ResultInputType is not null)
        {
            throw new InvalidOperationException("Results() may only start a result pipeline once per query.");
        }

        design.ResultInputType = typeof(TResult);
        return new RepositoryResultQueryBuilder<TResult>(design);
    }

    /// <summary>Projects and starts a typed result pipeline. ProjectTo() retains its entity-level API.</summary>
    public RepositoryResultQueryBuilder<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> projection) =>
        ProjectTo(projection).Results<TResult>();

    public RepositoryResultQueryBuilder<TResult> Select<TResult, TParameter>(Expression<Func<TEntity, TParameter, TResult>> projection) =>
        Results<TEntity>().Select(projection);

    /// <summary>Controls the entity's default ORDER BY for this named query. Explicit sorts are unaffected.</summary>
    public RepositoryQueryBuilder<TEntity> UseDefaultSort(bool enabled = true)
    {
        design.UseDefaultSort = enabled;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> AsTracking()
    {
        design.TrackingBehavior = RepositoryTrackingBehavior.Tracking;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> AsNoTracking()
    {
        design.TrackingBehavior = RepositoryTrackingBehavior.NoTracking;
        return this;
    }

    /// <summary>Configures a no-tracking read at design time, without exposing a runtime tracking method.</summary>
    public RepositoryQueryBuilder<TEntity> AsReadOnly(bool identityResolution = false)
    {
        design.TrackingBehavior = identityResolution
            ? RepositoryTrackingBehavior.NoTrackingWithIdentityResolution
            : RepositoryTrackingBehavior.NoTracking;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> AsNoTrackingWithIdentityResolution()
    {
        design.TrackingBehavior = RepositoryTrackingBehavior.NoTrackingWithIdentityResolution;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> AsSplitQuery()
    {
        design.SplittingBehavior = RepositoryQuerySplittingBehavior.SplitQuery;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> AsSingleQuery()
    {
        design.SplittingBehavior = RepositoryQuerySplittingBehavior.SingleQuery;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> IgnoreQueryFilters(bool ignore = true)
    {
        design.IgnoreQueryFilters = ignore;
        return this;
    }

    public RepositoryQueryBuilder<TEntity> TagWith(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new ArgumentException("A query tag is required.", nameof(tag));
        }

        design.QueryTag = tag.Trim();
        return this;
    }

    public RepositoryQueryBuilder<TEntity> Cache(
        Action<RepositoryQueryCacheBuilder>? configure = null)
    {
        design.Cache.Enabled = true;
        configure?.Invoke(new RepositoryQueryCacheBuilder(design.Cache));
        return this;
    }

    public RepositoryQueryBuilder<TEntity> List() => HasResult(RepositoryQueryResultKind.List);

    public RepositoryQueryBuilder<TEntity> Single() => HasResult(RepositoryQueryResultKind.Single);

    public RepositoryQueryBuilder<TEntity> SingleOrDefault() => HasResult(RepositoryQueryResultKind.SingleOrDefault);

    public RepositoryQueryBuilder<TEntity> FirstOrDefault() => HasResult(RepositoryQueryResultKind.FirstOrDefault);

    public RepositoryQueryBuilder<TEntity> Paged() => HasResult(RepositoryQueryResultKind.Paged);

    public RepositoryQueryBuilder<TEntity> Exists() => HasResult(RepositoryQueryResultKind.Exists);

    public RepositoryQueryBuilder<TEntity> Count() => HasResult(RepositoryQueryResultKind.Count);

    public RepositoryQueryBuilder<TEntity> Stream() => HasResult(RepositoryQueryResultKind.Stream);

    public RepositoryQueryBuilder<TEntity> CursorPaged<TCursor>(
        Expression<Func<TEntity, TCursor, bool>> seekPredicate,
        Expression<Func<TEntity, TCursor>> cursorSelector)
    {
        if (seekPredicate is null)
        {
            throw new ArgumentNullException(nameof(seekPredicate));
        }

        if (cursorSelector is null)
        {
            throw new ArgumentNullException(nameof(cursorSelector));
        }

        design.Cursor = new RepositoryCursorDesign(typeof(TCursor), seekPredicate, cursorSelector);
        return HasResult(RepositoryQueryResultKind.CursorPaged);
    }

    public RepositoryQueryBuilder<TEntity> GroupBy<TKey, TResult>(
        Expression<Func<TEntity, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, TEntity>, TResult>> projection)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        design.Projection = null;
        design.ProjectionTypeReference = null;
        design.ProjectionMethodNameReference = null;
        design.Group = new RepositoryGroupedQueryDesign(typeof(TResult), keySelector, projection);
        return HasResult(RepositoryQueryResultKind.GroupedList);
    }

    private RepositoryQueryBuilder<TEntity> AddPredicate(LambdaExpression predicate)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        design.PredicatesList.Add(predicate);
        return this;
    }

    public RepositoryQueryBuilder<TEntity> GroupBy<TKey, TResult, TRequest>(Expression<Func<TEntity, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, TEntity>, TRequest, TResult>> projection)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        if (design.ResultInputType is not null)
        {
            throw new InvalidOperationException("Configure entity grouping before Results().");
        }

        design.Group = new RepositoryGroupedQueryDesign(typeof(TResult), keySelector, projection);
        design.ResultKind = RepositoryQueryResultKind.GroupedList;
        if (design.Cursor is { IsRequestBound: true })
        {
            design.Cursor = null;
        }

        return this;
    }

    private RepositoryQueryBuilder<TEntity> AddSort(LambdaExpression keySelector, bool descending)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        design.SortsList.Add(new RepositorySortDesign(keySelector) { IsDescending = descending });
        return this;
    }

    private RepositoryQueryBuilder<TEntity> HasResult(RepositoryQueryResultKind resultKind)
    {
        design.ResultKind = resultKind;
        if (resultKind != RepositoryQueryResultKind.CursorPaged && design.Cursor is { IsRequestBound: true })
        {
            design.Cursor = null;
        }

        design.Dictionary = null;
        design.Scalar = null;
        return this;
    }

    private static string GetDirectPropertyName(Expression<Func<TEntity, object?>> expression)
    {
        Expression body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression { Member: PropertyInfo property } member ||
            member.Expression != expression.Parameters[0])
        {
            throw new ArgumentException("The expression must select one direct entity property.", nameof(expression));
        }

        return property.Name;
    }

    private static string RequireParameterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A parameter name is required.", nameof(name));
        }

        return name.Trim();
    }
}
