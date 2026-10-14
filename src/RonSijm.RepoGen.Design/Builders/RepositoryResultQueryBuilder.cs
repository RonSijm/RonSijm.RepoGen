using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

#pragma warning disable CA1720 // Single mirrors LINQ's established cardinality terminology.

/// <summary>Design-time clauses over a projected or grouped row. No runtime query API is exposed.</summary>
public sealed partial class RepositoryResultQueryBuilder<TResult>
{
    private readonly RepositoryQueryDesign design;
    private int stageCount;
    private bool superseded;

    internal RepositoryResultQueryBuilder(RepositoryQueryDesign design)
    {
        this.design = design;
        stageCount = design.ResultStages.Count;
    }

    /// <summary>Overrides the named closed query's SQL-command timeout. Null uses the context setting, not an inherited timeout.</summary>
    public RepositoryResultQueryBuilder<TResult> UseCommandTimeout(int? seconds)
    {
        RequireCurrentStage();
        design.CommandTimeout = new RepositoryCommandTimeoutDesign(seconds);
        return this;
    }

    /// <summary>Removes the whole named query's override and inherits its entity/model setting again.</summary>
    public RepositoryResultQueryBuilder<TResult> InheritCommandTimeout()
    {
        RequireCurrentStage();
        design.CommandTimeout = null;
        return this;
    }

    /// <summary>Exposes this named read only on the write capability. Does not save, lock rows or authorize access.</summary>
    public RepositoryResultQueryBuilder<TResult> OnWriteRepository(bool enabled = true)
    {
        RequireCurrentStage();
        design.Target = enabled ? RepositoryQueryTarget.Write : RepositoryQueryTarget.Read;
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> RequireNavigationVisibilityReview(bool? enabled = true)
    {
        RequireCurrentStage();
        design.NavigationVisibility.Required = enabled;
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> ReviewNavigationVisibility<TTarget>(string policyName, string reason) where TTarget : class
    {
        RequireCurrentStage();
        design.NavigationVisibility.Review<TTarget>(policyName, reason);
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> Where(Expression<Func<TResult, bool>> predicate) =>
        Add(RepositoryQueryStageKind.Where, predicate);

    public RepositoryResultQueryBuilder<TResult> Where<TParameter>(Expression<Func<TResult, TParameter, bool>> predicate) =>
        Add(RepositoryQueryStageKind.Where, predicate);

    /// <summary>Reuses an approved predicate over the current projected, grouped or scalar row.</summary>
    public RepositoryResultQueryBuilder<TResult> Use(RepositoryQueryFragment<TResult> fragment) =>
        Add(RepositoryQueryStageKind.Where, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate);

    public RepositoryResultQueryBuilder<TResult> Use<TRequest>(RepositoryQueryFragment<TResult, TRequest> fragment) =>
        Add(RepositoryQueryStageKind.Where, (fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate);

    /// <summary>Conditionally reuses a predicate, sharing the condition's request name with the fragment.</summary>
    public RepositoryResultQueryBuilder<TResult> UseIf<TRequest>(Expression<Func<TRequest, bool>> condition, RepositoryQueryFragment<TResult, TRequest> fragment)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        return Add(RepositoryQueryStageKind.Where, RepositoryFragmentExpressions.BindRequest(fragment.Design.Predicate, condition.Parameters[0]), condition);
    }

    public RepositoryResultQueryBuilder<TResult> UseIf<TRequest>(Expression<Func<TRequest, bool>> condition, RepositoryQueryFragment<TResult> fragment)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        return Add(RepositoryQueryStageKind.Where, RepositoryFragmentExpressions.BindRequest(fragment.Design.Predicate, condition.Parameters[0]), condition);
    }

    public RepositoryResultQueryBuilder<TResult> WhereIn<TValue, TRequest>(Expression<Func<TResult, TValue>> keySelector,
        Expression<Func<TRequest, IEnumerable<TValue>?>> values, Action<RepositoryMembershipOptionsBuilder>? configure = null) =>
        AddMembership(RepositoryMembershipDesign.Create(keySelector, values, negated: false, configure));

    public RepositoryResultQueryBuilder<TResult> WhereNotIn<TValue, TRequest>(Expression<Func<TResult, TValue>> keySelector,
        Expression<Func<TRequest, IEnumerable<TValue>?>> values, Action<RepositoryMembershipOptionsBuilder>? configure = null) =>
        AddMembership(RepositoryMembershipDesign.Create(keySelector, values, negated: true, configure));

    public RepositoryResultQueryBuilder<TResult> WhereIf<TParameter>(
        Expression<Func<TParameter, bool>> condition,
        Expression<Func<TResult, TParameter, bool>> predicate)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        return Add(RepositoryQueryStageKind.Where, predicate, condition);
    }

    public RepositoryResultQueryBuilder<TResult> OrderBy<TKey>(Expression<Func<TResult, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.OrderBy, keySelector);

    public RepositoryResultQueryBuilder<TResult> OrderBy<TKey, TParameter>(Expression<Func<TResult, TParameter, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.OrderBy, keySelector);

    public RepositoryResultQueryBuilder<TResult> OrderByDescending<TKey>(Expression<Func<TResult, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.OrderByDescending, keySelector);

    public RepositoryResultQueryBuilder<TResult> OrderByDescending<TKey, TParameter>(Expression<Func<TResult, TParameter, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.OrderByDescending, keySelector);

    public RepositoryResultQueryBuilder<TResult> ThenBy<TKey>(Expression<Func<TResult, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.ThenBy, keySelector);

    public RepositoryResultQueryBuilder<TResult> ThenBy<TKey, TParameter>(Expression<Func<TResult, TParameter, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.ThenBy, keySelector);

    public RepositoryResultQueryBuilder<TResult> ThenByDescending<TKey>(Expression<Func<TResult, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.ThenByDescending, keySelector);

    public RepositoryResultQueryBuilder<TResult> ThenByDescending<TKey, TParameter>(Expression<Func<TResult, TParameter, TKey>> keySelector) =>
        Add(RepositoryQueryStageKind.ThenByDescending, keySelector);

    /// <summary>Replaces result ordering using only designer-whitelisted aliases. Invalid fields fail before context acquisition.</summary>
    public RepositoryResultQueryBuilder<TResult> OrderByField<TRequest>(Expression<Func<TRequest, string?>> fieldName,
        Expression<Func<TRequest, bool>> descending, Action<RepositorySortFieldsBuilder<TResult>> configure)
    {
        if (fieldName is null)
        {
            throw new ArgumentNullException(nameof(fieldName));
        }

        if (descending is null)
        {
            throw new ArgumentNullException(nameof(descending));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        RequireCurrentStage();
        var fields = new RepositorySortFieldsBuilder<TResult>();
        configure(fields);
        var sort = fields.Build(fieldName, RepositoryFragmentExpressions.BindInput(descending, fieldName.Parameters[0]));
        // Configuration is captured independently from the callback's mutable builder.
        RequireCurrentStage();
        design.ResultStagesList.Add(new RepositoryQueryStageDesign(RepositoryQueryStageKind.OrderByField, typeof(TResult), null, fieldSort: sort));
        stageCount++;
        return this;
    }

    /// <summary>Replaces ordering only when the input condition is true. Configure an unconditional fallback order for guarded paging/ThenBy.</summary>
    public RepositoryResultQueryBuilder<TResult> OrderByIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.OrderBy, condition, keySelector);

    public RepositoryResultQueryBuilder<TResult> OrderByIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TRequest, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.OrderBy, condition, keySelector);

    public RepositoryResultQueryBuilder<TResult> OrderByDescendingIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.OrderByDescending, condition, keySelector);

    public RepositoryResultQueryBuilder<TResult> OrderByDescendingIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TRequest, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.OrderByDescending, condition, keySelector);

    /// <summary>Appends a tie-breaker only when enabled. Requires adjacent ordering guaranteed on every branch.</summary>
    public RepositoryResultQueryBuilder<TResult> ThenByIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.ThenBy, condition, keySelector);

    public RepositoryResultQueryBuilder<TResult> ThenByIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TRequest, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.ThenBy, condition, keySelector);

    public RepositoryResultQueryBuilder<TResult> ThenByDescendingIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.ThenByDescending, condition, keySelector);

    public RepositoryResultQueryBuilder<TResult> ThenByDescendingIf<TKey, TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TResult, TRequest, TKey>> keySelector) =>
        AddConditional(RepositoryQueryStageKind.ThenByDescending, condition, keySelector);

    public RepositoryResultQueryBuilder<TNext> Select<TNext>(Expression<Func<TResult, TNext>> projection)
    {
        Add(RepositoryQueryStageKind.Select, projection);
        superseded = true;
        return new RepositoryResultQueryBuilder<TNext>(design);
    }

    public RepositoryResultQueryBuilder<TNext> Select<TNext, TParameter>(Expression<Func<TResult, TParameter, TNext>> projection)
    {
        Add(RepositoryQueryStageKind.Select, projection);
        superseded = true;
        return new RepositoryResultQueryBuilder<TNext>(design);
    }

    public RepositoryResultQueryBuilder<TNext> GroupBy<TKey, TNext>(
        Expression<Func<TResult, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, TResult>, TNext>> projection)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        RequireCurrentStage();
        design.ResultStagesList.Add(new RepositoryQueryStageDesign(RepositoryQueryStageKind.GroupBy, typeof(TResult), keySelector, projection: projection));
        stageCount++;
        superseded = true;
        return new RepositoryResultQueryBuilder<TNext>(design);
    }

    public RepositoryResultQueryBuilder<TResult> Distinct() => Add(RepositoryQueryStageKind.Distinct);

    /// <summary>De-duplicates projected rows only when enabled. Configure paging order after this stage.</summary>
    public RepositoryResultQueryBuilder<TResult> DistinctIf<TRequest>(Expression<Func<TRequest, bool>> condition)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        return Add(RepositoryQueryStageKind.Distinct, condition: condition);
    }

    public RepositoryResultQueryBuilder<TNext> GroupBy<TKey, TNext, TRequest>(Expression<Func<TResult, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, TResult>, TRequest, TNext>> projection)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        RequireCurrentStage();
        design.ResultStagesList.Add(new RepositoryQueryStageDesign(RepositoryQueryStageKind.GroupBy, typeof(TResult), keySelector, projection: projection));
        stageCount++;
        superseded = true;
        return new RepositoryResultQueryBuilder<TNext>(design);
    }

    public RepositoryResultQueryBuilder<TResult> Skip(int count) =>
        Add(RepositoryQueryStageKind.Skip, count: RequireCount(count));

    public RepositoryResultQueryBuilder<TResult> Skip<TParameter>(Expression<Func<TParameter, int>> count) =>
        Add(RepositoryQueryStageKind.Skip, count);

    public RepositoryResultQueryBuilder<TResult> Take(int count) =>
        Add(RepositoryQueryStageKind.Take, count: RequireCount(count));

    public RepositoryResultQueryBuilder<TResult> Take<TParameter>(Expression<Func<TParameter, int>> count) =>
        Add(RepositoryQueryStageKind.Take, count);

    /// <summary>Omits the stage when disabled. The count expression is evaluated and validated only on the enabled branch.</summary>
    public RepositoryResultQueryBuilder<TResult> SkipIf<TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TRequest, int>> count) =>
        AddConditionalCount(RepositoryQueryStageKind.Skip, condition, count);

    public RepositoryResultQueryBuilder<TResult> SkipIf<TRequest>(Expression<Func<TRequest, bool>> condition, int count) =>
        AddConditionalCount(RepositoryQueryStageKind.Skip, condition, count: RequireCount(count));

    public RepositoryResultQueryBuilder<TResult> TakeIf<TRequest>(Expression<Func<TRequest, bool>> condition, Expression<Func<TRequest, int>> count) =>
        AddConditionalCount(RepositoryQueryStageKind.Take, condition, count);

    public RepositoryResultQueryBuilder<TResult> TakeIf<TRequest>(Expression<Func<TRequest, bool>> condition, int count) =>
        AddConditionalCount(RepositoryQueryStageKind.Take, condition, count: RequireCount(count));

    public RepositoryResultQueryBuilder<TResult> List() => Terminal(RepositoryQueryResultKind.List);
    public RepositoryResultQueryBuilder<TResult> Single() => Terminal(RepositoryQueryResultKind.Single);
    public RepositoryResultQueryBuilder<TResult> SingleOrDefault() => Terminal(RepositoryQueryResultKind.SingleOrDefault);
    public RepositoryResultQueryBuilder<TResult> FirstOrDefault() => Terminal(RepositoryQueryResultKind.FirstOrDefault);
    public RepositoryResultQueryBuilder<TResult> Paged() => Terminal(RepositoryQueryResultKind.Paged);

    public RepositoryResultQueryBuilder<TResult> Paged(RepositoryPageConsistency consistency)
    {
        if (consistency is < RepositoryPageConsistency.BestEffort or > RepositoryPageConsistency.Serializable)
        {
            throw new ArgumentOutOfRangeException(nameof(consistency));
        }

        design.PageConsistency = consistency;
        return Terminal(RepositoryQueryResultKind.Paged);
    }

    public RepositoryResultQueryBuilder<TResult> RequirePagingOrder(bool enabled = true)
    {
        RequireCurrentStage();
        design.RequirePagingOrder = enabled;
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> AllowNullInput<TInput>(string parameterName) where TInput : class?
    {
        RequireCurrentStage();
        design.InputNullability = design.InputNullability.Concat(new[] { new RepositoryInputNullabilityDesign(parameterName, typeof(TInput)) }).ToArray();
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> InputNullability<TInput>(string parameterName, Action<RepositoryTypeNullabilityBuilder> configure)
    {
        RequireCurrentStage();
        design.InputNullability = design.InputNullability.Concat(new[]
        {
            new RepositoryInputNullabilityDesign(parameterName,
                RepositoryTypeNullabilityBuilder.Create(typeof(TInput), configure))
        }).ToArray();
        return this;
    }

    /// <summary>Annotates the final row, dictionary/lookup value, or aggregate value. TValue must match that final CLR type.</summary>
    public RepositoryResultQueryBuilder<TResult> ResultNullability<TValue>(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        RequireCurrentStage();
        if (design.ResultNullability is not null)
        {
            throw new InvalidOperationException($"RGN0005: Query '{design.MethodName}' configures result nullability more than once.");
        }

        design.ResultNullability = RepositoryTypeNullabilityBuilder.Create(typeof(TValue), configure);
        return this;
    }

    /// <summary>Annotates a dictionary/lookup key independently from ResultNullability (its value).</summary>
    public RepositoryResultQueryBuilder<TResult> KeyNullability<TKey>(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        RequireCurrentStage();
        if (design.DictionaryKeyNullability is not null)
        {
            throw new InvalidOperationException($"RGN0005: Query '{design.MethodName}' configures key nullability more than once.");
        }

        design.DictionaryKeyNullability = RepositoryTypeNullabilityBuilder.Create(typeof(TKey), configure);
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> CursorPaged<TCursor>(Expression<Func<TResult, TCursor, bool>> seekPredicate, Expression<Func<TResult, TCursor>> cursorSelector)
    {
        if (seekPredicate is null)
        {
            throw new ArgumentNullException(nameof(seekPredicate));
        }

        if (cursorSelector is null)
        {
            throw new ArgumentNullException(nameof(cursorSelector));
        }

        Terminal(RepositoryQueryResultKind.CursorPaged);
        design.Cursor = new RepositoryCursorDesign(typeof(TCursor), seekPredicate, cursorSelector) { OnResults = true };
        design.RequirePagingOrder = true;
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> Slice() => Terminal(RepositoryQueryResultKind.Slice);
    public RepositoryResultQueryBuilder<TResult> Count() => Terminal(RepositoryQueryResultKind.Count);
    public RepositoryResultQueryBuilder<TResult> Exists() => Terminal(RepositoryQueryResultKind.Exists);
    public RepositoryResultQueryBuilder<TResult> Stream() => Terminal(RepositoryQueryResultKind.Stream);

    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey>(Expression<Func<TResult, TKey>> keySelector)
        where TKey : notnull => Dictionary(keySelector, row => row);

    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey>(Expression<Func<TResult, TKey>> keySelector,
        RepositoryDuplicateKeyBehavior duplicateKeys)
        where TKey : notnull => Dictionary(keySelector, row => row, duplicateKeys);

    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey, TValue>(
        Expression<Func<TResult, TKey>> keySelector,
        Expression<Func<TResult, TValue>> valueSelector)
        where TKey : notnull => Dictionary(keySelector, valueSelector, RepositoryDuplicateKeyBehavior.Throw);

    /// <summary>Selects approved key/value columns, then applies the explicit collision policy after materialization.</summary>
    public RepositoryResultQueryBuilder<TResult> Dictionary<TKey, TValue>(
        Expression<Func<TResult, TKey>> keySelector,
        Expression<Func<TResult, TValue>> valueSelector,
        RepositoryDuplicateKeyBehavior duplicateKeys)
        where TKey : notnull
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (valueSelector is null)
        {
            throw new ArgumentNullException(nameof(valueSelector));
        }

        if (duplicateKeys is < RepositoryDuplicateKeyBehavior.Throw or > RepositoryDuplicateKeyBehavior.KeepLast)
        {
            throw new ArgumentOutOfRangeException(nameof(duplicateKeys), "RGD2501: Unknown dictionary duplicate-key behavior.");
        }

        Terminal(RepositoryQueryResultKind.Dictionary);
        design.Dictionary = new RepositoryDictionaryDesign(keySelector, valueSelector, duplicateKeys);
        return this;
    }

    /// <summary>Materializes approved key/value columns once, then groups duplicate keys without discarding values.</summary>
    public RepositoryResultQueryBuilder<TResult> Lookup<TKey>(Expression<Func<TResult, TKey>> keySelector)
        where TKey : notnull => Lookup(keySelector, row => row);

    public RepositoryResultQueryBuilder<TResult> Lookup<TKey, TValue>(Expression<Func<TResult, TKey>> keySelector, Expression<Func<TResult, TValue>> valueSelector)
        where TKey : notnull
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        if (valueSelector is null)
        {
            throw new ArgumentNullException(nameof(valueSelector));
        }

        Terminal(RepositoryQueryResultKind.Lookup);
        design.Dictionary = new RepositoryDictionaryDesign(keySelector, valueSelector);
        return this;
    }

    public RepositoryResultQueryBuilder<TResult> Max<TValue>(Expression<Func<TResult, TValue>> selector) =>
        Scalar(RepositoryQueryResultKind.Max, selector, typeof(TValue));

    public RepositoryResultQueryBuilder<TResult> Min<TValue>(Expression<Func<TResult, TValue>> selector) =>
        Scalar(RepositoryQueryResultKind.Min, selector, typeof(TValue));

    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, int>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(int));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, long>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(long));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, float>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(float));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, double>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, decimal>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(decimal));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, int?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(int?));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, long?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(long?));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, float?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(float?));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, double?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Sum(Expression<Func<TResult, decimal?>> selector) => Scalar(RepositoryQueryResultKind.Sum, selector, typeof(decimal?));

    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, int>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, long>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, float>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(float));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, double>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, decimal>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(decimal));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, int?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, long?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, float?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(float?));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, double?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(double?));
    public RepositoryResultQueryBuilder<TResult> Average(Expression<Func<TResult, decimal?>> selector) => Scalar(RepositoryQueryResultKind.Average, selector, typeof(decimal?));

    private RepositoryResultQueryBuilder<TResult> Scalar(RepositoryQueryResultKind kind, LambdaExpression selector, Type returnType)
    {
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        Terminal(kind);
        design.Scalar = new RepositoryScalarDesign(selector, returnType);
        return this;
    }

    private RepositoryResultQueryBuilder<TResult> Terminal(RepositoryQueryResultKind kind)
    {
        RequireCurrentStage();
        design.ResultKind = kind;
        if (kind != RepositoryQueryResultKind.CursorPaged && design.Cursor is { IsRequestBound: true })
        {
            design.Cursor = null;
        }

        design.Dictionary = null;
        design.Scalar = null;
        return this;
    }

    private RepositoryResultQueryBuilder<TResult> Add(
        RepositoryQueryStageKind kind, LambdaExpression? expression = null,
        LambdaExpression? condition = null, int? count = null)
    {
        RequireCurrentStage();
        if (kind != RepositoryQueryStageKind.Distinct && expression is null && count is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        design.ResultStagesList.Add(new RepositoryQueryStageDesign(kind, typeof(TResult), expression, condition, count));
        stageCount++;
        return this;
    }

    private RepositoryResultQueryBuilder<TResult> AddConditional<TRequest>(RepositoryQueryStageKind kind,
        Expression<Func<TRequest, bool>> condition, LambdaExpression keySelector)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        return Add(kind, keySelector.Parameters.Count == 1 ? keySelector : RepositoryFragmentExpressions.BindRequest(keySelector, condition.Parameters[0]), condition);
    }

    private RepositoryResultQueryBuilder<TResult> AddConditionalCount<TRequest>(RepositoryQueryStageKind kind,
        Expression<Func<TRequest, bool>> condition, LambdaExpression? expression = null, int? count = null)
    {
        if (condition is null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        if (expression is null && count is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        return Add(kind, expression is null ? null : RepositoryFragmentExpressions.BindInput(expression, condition.Parameters[0]), condition, count);
    }

    private RepositoryResultQueryBuilder<TResult> AddMembership(RepositoryMembershipDesign membership)
    {
        RequireCurrentStage();
        design.ResultStagesList.Add(new RepositoryQueryStageDesign(RepositoryQueryStageKind.Membership, typeof(TResult), null, membership: membership));
        stageCount++;
        return this;
    }

    private void RequireCurrentStage()
    {
        var currentType = design.ResultStages.Count == 0 ? design.ResultInputType : design.ResultStages[design.ResultStages.Count - 1].OutputType;
        if (superseded || stageCount != design.ResultStages.Count || currentType != typeof(TResult))
        {
            throw new InvalidOperationException("This result builder is stale. Continue from the most recent Select()/GroupBy()/Results() stage.");
        }
    }

    private static int RequireCount(int count) => count >= 0 ? count : throw new ArgumentOutOfRangeException(nameof(count));
}
