using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

#pragma warning disable CA1720 // Single mirrors LINQ's established cardinality terminology.

namespace RonSijm.RepoGen.Design;

public abstract class RepositoryDesigner
{
    public RepositoryDesign CreateModel()
    {
        var modelBuilder = new RepositoryModelBuilder();
        OnModelCreating(modelBuilder);
        return modelBuilder.Build();
    }

    protected abstract void OnModelCreating(RepositoryModelBuilder modelBuilder);
}

public sealed class RepositoryModelBuilder
{
    private readonly Dictionary<Type, RepositoryEntityDesign> entities = new();
    private readonly RepositoryGenerationOptions options = new();

    public RepositoryModelBuilder Configure(Action<RepositoryGenerationOptionsBuilder> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(new RepositoryGenerationOptionsBuilder(options));
        return this;
    }

    public RepositoryEntityBuilder<TEntity> Entity<TEntity>()
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        if (!entities.TryGetValue(entityType, out var entity))
        {
            entity = new RepositoryEntityDesign(entityType);
            entities.Add(entityType, entity);
        }

        return new RepositoryEntityBuilder<TEntity>(entity);
    }

    public RepositoryModelBuilder Entity<TEntity>(Action<RepositoryEntityBuilder<TEntity>> configure)
        where TEntity : class
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(Entity<TEntity>());
        return this;
    }

    internal RepositoryDesign Build() => new(
        entities.Values.OrderBy(entity => entity.EntityType.FullName).ToArray(),
        options);
}

public sealed class RepositoryGenerationOptionsBuilder
{
    private readonly RepositoryGenerationOptions options;

    internal RepositoryGenerationOptionsBuilder(RepositoryGenerationOptions options)
    {
        this.options = options;
    }

    public RepositoryGenerationOptionsBuilder UseDbContextResolver(bool enabled = true)
    {
        options.UseDbContextResolver = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateCacheDecorator(bool enabled = true)
    {
        options.GenerateCacheDecorator = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateBatchOperations(bool enabled = true)
    {
        options.GenerateBatchOperations = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateSetBasedOperations(bool enabled = true)
    {
        options.GenerateSetBasedOperations = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateConcurrencyRetry(bool enabled = true)
    {
        options.GenerateConcurrencyRetry = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateTransactions(bool enabled = true)
    {
        options.GenerateTransactions = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder GenerateSynchronousMethods(bool enabled = true)
    {
        options.GenerateSynchronousMethods = enabled;
        return this;
    }

    public RepositoryGenerationOptionsBuilder UseDefaultTracking(RepositoryTrackingBehavior behavior)
    {
        options.DefaultTrackingBehavior = behavior;
        return this;
    }
}

public sealed class RepositoryGenerationOptions
{
    public bool UseDbContextResolver { get; internal set; }

    public bool GenerateCacheDecorator { get; internal set; } = true;

    public bool GenerateBatchOperations { get; internal set; } = true;

    public bool GenerateSetBasedOperations { get; internal set; }

    public bool GenerateConcurrencyRetry { get; internal set; } = true;

    public bool GenerateTransactions { get; internal set; } = true;

    public bool GenerateSynchronousMethods { get; internal set; } = true;

    public RepositoryTrackingBehavior DefaultTrackingBehavior { get; internal set; }
}

public sealed class RepositoryEntityBuilder<TEntity>
    where TEntity : class
{
    private readonly RepositoryEntityDesign design;

    internal RepositoryEntityBuilder(RepositoryEntityDesign design)
    {
        this.design = design;
    }

    public RepositoryAccessorBuilder HasAccessor<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        var propertyInfo = GetProperty(property);
        var accessor = design.AccessorsList.FirstOrDefault(item => item.PropertyName == propertyInfo.Name);
        if (accessor is null)
        {
            accessor = new RepositoryAccessorDesign(propertyInfo.Name);
            design.AccessorsList.Add(accessor);
        }

        return new RepositoryAccessorBuilder(accessor);
    }

    public RepositoryEntityBuilder<TEntity> Exclude<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        design.ExcludedPropertiesSet.Add(GetProperty(property).Name);
        return this;
    }

    public RepositoryProjectionBuilder<TEntity> ProjectTo<TProjection>(Expression<Func<TEntity, TProjection>> projection)
    {
        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        var projectionDesign = new RepositoryProjectionDesign(typeof(TProjection), projection);
        design.ProjectionsList.Add(projectionDesign);
        return new RepositoryProjectionBuilder<TEntity>(projectionDesign);
    }

    public RepositoryQueryBuilder<TEntity> HasQuery(string methodName)
    {
        var query = new RepositoryQueryDesign(RequireMethodName(methodName));
        design.QueriesList.Add(query);
        return new RepositoryQueryBuilder<TEntity>(query);
    }

    public RepositoryCommandBuilder<TEntity> HasCommand(string methodName)
    {
        var command = new RepositoryCommandDesign(RequireMethodName(methodName));
        design.CommandsList.Add(command);
        return new RepositoryCommandBuilder<TEntity>(command);
    }

    public RepositoryQueryFragment<TEntity> HasFragment(
        string name,
        Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var fragment = new RepositoryQueryFragment<TEntity>(RequireMethodName(name), predicate);
        design.FragmentsList.Add(fragment.Design);
        return fragment;
    }

    public RepositoryFilterBuilder HasFilter<TParameter>(
        string methodName,
        Expression<Func<TEntity, TParameter, bool>> predicate) =>
        AddFilter(methodName, predicate);

    public RepositoryFilterBuilder HasFilter<TParameter1, TParameter2>(
        string methodName,
        Expression<Func<TEntity, TParameter1, TParameter2, bool>> predicate) =>
        AddFilter(methodName, predicate);

    public RepositoryFilterBuilder HasFilter<TParameter1, TParameter2, TParameter3>(
        string methodName,
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, bool>> predicate) =>
        AddFilter(methodName, predicate);

    public RepositoryFilterBuilder HasFilter<TParameter1, TParameter2, TParameter3, TParameter4>(
        string methodName,
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TParameter4, bool>> predicate) =>
        AddFilter(methodName, predicate);

    public RepositorySortBuilder HasDefaultSort<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        if (keySelector is null)
        {
            throw new ArgumentNullException(nameof(keySelector));
        }

        var sort = new RepositorySortDesign(keySelector);
        design.DefaultSortsList.Add(sort);
        return new RepositorySortBuilder(sort);
    }

    public RepositoryEntityBuilder<TEntity> HasSortableFields(
        params Expression<Func<TEntity, object?>>[] properties)
    {
        if (properties is null)
        {
            throw new ArgumentNullException(nameof(properties));
        }

        if (properties.Length == 0)
        {
            throw new ArgumentException("At least one sortable property is required.", nameof(properties));
        }

        foreach (var property in properties)
        {
            var propertyName = GetProperty(property).Name;
            if (design.SortableFieldsList.All(item =>
                    !string.Equals(item.PropertyName, propertyName, StringComparison.Ordinal)))
            {
                design.SortableFieldsList.Add(new RepositorySortableFieldDesign(propertyName));
            }
        }

        return this;
    }

    public RepositoryEntityBuilder<TEntity> Include<TNavigation>(Expression<Func<TEntity, TNavigation>> navigation)
    {
        if (navigation is null)
        {
            throw new ArgumentNullException(nameof(navigation));
        }

        design.IncludesList.Add(new RepositoryIncludeDesign(navigation));
        return this;
    }

    private RepositoryFilterBuilder AddFilter(string methodName, LambdaExpression predicate)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("A method name is required.", nameof(methodName));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var filter = new RepositoryFilterDesign(methodName.Trim(), predicate);
        design.FiltersList.Add(filter);
        return new RepositoryFilterBuilder(filter);
    }

    private static PropertyInfo GetProperty<TProperty>(Expression<Func<TEntity, TProperty>> expression)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        Expression body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression { Member: PropertyInfo propertyInfo } member ||
            member.Expression != expression.Parameters[0])
        {
            throw new ArgumentException("The expression must select one direct entity property.", nameof(expression));
        }

        return propertyInfo;
    }

    private static string RequireMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("A method name is required.", nameof(methodName));
        }

        return methodName.Trim();
    }
}

public enum RepositoryQueryResultKind
{
    List,
    Single,
    SingleOrDefault,
    FirstOrDefault,
    Paged,
    CursorPaged,
    GroupedList,
    Exists,
    Count,
    Stream
}

public enum RepositoryTrackingBehavior
{
    Unspecified,
    Tracking,
    NoTracking,
    NoTrackingWithIdentityResolution
}

public enum RepositoryQuerySplittingBehavior
{
    Unspecified,
    SingleQuery,
    SplitQuery
}

public sealed class RepositoryQueryFragment<TEntity>
    where TEntity : class
{
    internal RepositoryQueryFragment(string name, LambdaExpression predicate)
    {
        Design = new RepositoryQueryFragmentDesign(name, predicate);
    }

    internal RepositoryQueryFragmentDesign Design { get; }

    public string Name => Design.Name;
}

public sealed class RepositoryQueryBuilder<TEntity>
    where TEntity : class
{
    private readonly RepositoryQueryDesign design;

    internal RepositoryQueryBuilder(RepositoryQueryDesign design)
    {
        this.design = design;
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

    public RepositoryQueryBuilder<TEntity> Where<TParameter>(
        Expression<Func<TEntity, TParameter, bool>> predicate) => AddPredicate(predicate);

    public RepositoryQueryBuilder<TEntity> Where<TParameter1, TParameter2>(
        Expression<Func<TEntity, TParameter1, TParameter2, bool>> predicate) => AddPredicate(predicate);

    public RepositoryQueryBuilder<TEntity> Where<TParameter1, TParameter2, TParameter3>(
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, bool>> predicate) => AddPredicate(predicate);

    public RepositoryQueryBuilder<TEntity> Where<TParameter1, TParameter2, TParameter3, TParameter4>(
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TParameter4, bool>> predicate) =>
        AddPredicate(predicate);

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

    public RepositoryQueryBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        AddSort(keySelector, descending: false);

    public RepositoryQueryBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        AddSort(keySelector, descending: true);

    public RepositoryQueryBuilder<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        AddSort(keySelector, descending: false);

    public RepositoryQueryBuilder<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
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
        design.Group = null;
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
        if (resultKind != RepositoryQueryResultKind.GroupedList)
        {
            design.Group = null;
        }

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

public sealed class RepositoryQueryCacheBuilder
{
    private readonly RepositoryQueryCacheDesign design;

    internal RepositoryQueryCacheBuilder(RepositoryQueryCacheDesign design)
    {
        this.design = design;
    }

    public RepositoryQueryCacheBuilder For(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        design.AbsoluteExpirationRelativeToNow = duration;
        return this;
    }

    public RepositoryQueryCacheBuilder Sliding(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        design.SlidingExpiration = duration;
        return this;
    }

    public RepositoryQueryCacheBuilder DependsOn<TDependentEntity>()
        where TDependentEntity : class
    {
        design.DependentEntityTypesSet.Add(typeof(TDependentEntity));
        return this;
    }
}

public sealed class RepositoryCommandBuilder<TEntity>
    where TEntity : class
{
    private readonly RepositoryCommandDesign design;

    internal RepositoryCommandBuilder(RepositoryCommandDesign design)
    {
        this.design = design;
    }

    public RepositoryCommandBuilder<TEntity> Use(RepositoryQueryFragment<TEntity> fragment)
    {
        if (fragment is null)
        {
            throw new ArgumentNullException(nameof(fragment));
        }

        design.PredicatesList.Add(fragment.Design.Predicate);
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate) =>
        AddPredicate(predicate);

    public RepositoryCommandBuilder<TEntity> Where<TParameter>(
        Expression<Func<TEntity, TParameter, bool>> predicate) => AddPredicate(predicate);

    public RepositoryCommandBuilder<TEntity> Where<TParameter1, TParameter2>(
        Expression<Func<TEntity, TParameter1, TParameter2, bool>> predicate) => AddPredicate(predicate);

    public RepositoryCommandBuilder<TEntity> Where<TParameter1, TParameter2, TParameter3>(
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, bool>> predicate) => AddPredicate(predicate);

    public RepositoryCommandBuilder<TEntity> Where<TParameter1, TParameter2, TParameter3, TParameter4>(
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TParameter4, bool>> predicate) =>
        AddPredicate(predicate);

    public RepositoryCommandBuilder<TEntity> Delete()
    {
        design.Kind = RepositoryCommandKind.Delete;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Update(Action<RepositoryUpdateBuilder<TEntity>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        design.Kind = RepositoryCommandKind.Update;
        configure(new RepositoryUpdateBuilder<TEntity>(design));
        return this;
    }

    private RepositoryCommandBuilder<TEntity> AddPredicate(LambdaExpression predicate)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        design.PredicatesList.Add(predicate);
        return this;
    }
}

public sealed class RepositoryUpdateBuilder<TEntity>
    where TEntity : class
{
    private readonly RepositoryCommandDesign design;

    internal RepositoryUpdateBuilder(RepositoryCommandDesign design)
    {
        this.design = design;
    }

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter1, TParameter2>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter1, TParameter2, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter1, TParameter2, TParameter3>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TProperty>> value)
        => AddUpdate(property, value);

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty, TParameter1, TParameter2, TParameter3, TParameter4>(
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TParameter1, TParameter2, TParameter3, TParameter4, TProperty>> value)
        => AddUpdate(property, value);

    private RepositoryUpdateBuilder<TEntity> AddUpdate<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        LambdaExpression value)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        design.UpdatesList.Add(new RepositoryUpdateDesign(property, value));
        return this;
    }

    public RepositoryUpdateBuilder<TEntity> SetProperty<TProperty>(
        Expression<Func<TEntity, TProperty>> property,
        TProperty value)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var constantValue = Expression.Constant(value, typeof(TProperty));
        var valueExpression = Expression.Lambda<Func<TEntity, TProperty>>(constantValue, parameter);
        return SetProperty(property, valueExpression);
    }
}

public sealed class RepositoryAccessorBuilder
{
    private readonly RepositoryAccessorDesign design;

    internal RepositoryAccessorBuilder(RepositoryAccessorDesign design)
    {
        this.design = design;
    }

    public RepositoryAccessorBuilder IsUnique(bool unique = true)
    {
        design.IsUnique = unique;
        return this;
    }

    public RepositoryAccessorBuilder HasMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("A method name is required.", nameof(methodName));
        }

        design.MethodName = methodName.Trim();
        return this;
    }
}

public class RepositoryProjectionBuilder
{
    private protected readonly RepositoryProjectionDesign Design;

    internal RepositoryProjectionBuilder(RepositoryProjectionDesign design)
    {
        Design = design;
    }

    public RepositoryProjectionBuilder HasMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("A method name is required.", nameof(methodName));
        }

        Design.MethodName = methodName.Trim();
        return this;
    }
}

public sealed class RepositoryProjectionBuilder<TEntity> : RepositoryProjectionBuilder
    where TEntity : class
{
    internal RepositoryProjectionBuilder(RepositoryProjectionDesign design)
        : base(design)
    {
    }

    public new RepositoryProjectionBuilder<TEntity> HasMethodName(string methodName)
    {
        base.HasMethodName(methodName);
        return this;
    }

    public RepositoryProjectionBuilder<TEntity> Include<TNavigation>(
        Expression<Func<TEntity, TNavigation>> navigation)
    {
        if (navigation is null)
        {
            throw new ArgumentNullException(nameof(navigation));
        }

        Design.IncludesList.Add(new RepositoryIncludeDesign(navigation));
        return this;
    }
}

public sealed class RepositoryFilterBuilder
{
    private readonly RepositoryFilterDesign design;

    internal RepositoryFilterBuilder(RepositoryFilterDesign design)
    {
        this.design = design;
    }

    public RepositoryFilterBuilder IsUnique(bool unique = true)
    {
        design.IsUnique = unique;
        return this;
    }
}

public sealed class RepositorySortBuilder
{
    private readonly RepositorySortDesign design;

    internal RepositorySortBuilder(RepositorySortDesign design)
    {
        this.design = design;
    }

    public RepositorySortBuilder Descending(bool descending = true)
    {
        design.IsDescending = descending;
        return this;
    }
}

public sealed class RepositoryDesign
{
    internal RepositoryDesign(
        IReadOnlyList<RepositoryEntityDesign> entities,
        RepositoryGenerationOptions options)
    {
        Entities = entities;
        Options = options;
    }

    public IReadOnlyList<RepositoryEntityDesign> Entities { get; }

    public RepositoryGenerationOptions Options { get; }
}

public sealed class RepositoryEntityDesign
{
    private readonly HashSet<string> excludedProperties = new(StringComparer.Ordinal);
    private readonly List<RepositoryAccessorDesign> accessors = new();
    private readonly List<RepositoryFilterDesign> filters = new();
    private readonly List<RepositoryIncludeDesign> includes = new();
    private readonly List<RepositoryProjectionDesign> projections = new();
    private readonly List<RepositorySortDesign> defaultSorts = new();
    private readonly List<RepositorySortableFieldDesign> sortableFields = new();
    private readonly List<RepositoryQueryDesign> queries = new();
    private readonly List<RepositoryCommandDesign> commands = new();
    private readonly List<RepositoryQueryFragmentDesign> fragments = new();

    internal RepositoryEntityDesign(Type entityType)
    {
        EntityType = entityType;
    }

    public Type EntityType { get; }

    public IReadOnlyCollection<string> ExcludedProperties => excludedProperties;

    public IReadOnlyList<RepositoryAccessorDesign> Accessors => new ReadOnlyCollection<RepositoryAccessorDesign>(accessors);

    public IReadOnlyList<RepositoryProjectionDesign> Projections => new ReadOnlyCollection<RepositoryProjectionDesign>(projections);

    public IReadOnlyList<RepositoryFilterDesign> Filters => new ReadOnlyCollection<RepositoryFilterDesign>(filters);

    public IReadOnlyList<RepositorySortDesign> DefaultSorts => new ReadOnlyCollection<RepositorySortDesign>(defaultSorts);

    public IReadOnlyList<RepositorySortableFieldDesign> SortableFields =>
        new ReadOnlyCollection<RepositorySortableFieldDesign>(sortableFields);

    public IReadOnlyList<RepositoryIncludeDesign> Includes => new ReadOnlyCollection<RepositoryIncludeDesign>(includes);

    public IReadOnlyList<RepositoryQueryDesign> Queries => new ReadOnlyCollection<RepositoryQueryDesign>(queries);

    public IReadOnlyList<RepositoryCommandDesign> Commands => new ReadOnlyCollection<RepositoryCommandDesign>(commands);

    public IReadOnlyList<RepositoryQueryFragmentDesign> Fragments =>
        new ReadOnlyCollection<RepositoryQueryFragmentDesign>(fragments);

    internal HashSet<string> ExcludedPropertiesSet => excludedProperties;

    internal List<RepositoryAccessorDesign> AccessorsList => accessors;

    internal List<RepositoryProjectionDesign> ProjectionsList => projections;

    internal List<RepositoryFilterDesign> FiltersList => filters;

    internal List<RepositorySortDesign> DefaultSortsList => defaultSorts;

    internal List<RepositorySortableFieldDesign> SortableFieldsList => sortableFields;

    internal List<RepositoryIncludeDesign> IncludesList => includes;

    internal List<RepositoryQueryDesign> QueriesList => queries;

    internal List<RepositoryCommandDesign> CommandsList => commands;

    internal List<RepositoryQueryFragmentDesign> FragmentsList => fragments;
}

public sealed class RepositoryAccessorDesign
{
    internal RepositoryAccessorDesign(string propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; }

    public bool IsUnique { get; internal set; }

    public string? MethodName { get; internal set; }
}

public sealed class RepositoryProjectionDesign
{
    private readonly List<RepositoryIncludeDesign> includes = new();

    internal RepositoryProjectionDesign(Type projectionType, LambdaExpression expression)
    {
        ProjectionType = projectionType;
        Expression = expression;
    }

    public Type ProjectionType { get; }

    public LambdaExpression Expression { get; }

    public string? MethodName { get; internal set; }

    public IReadOnlyList<RepositoryIncludeDesign> Includes =>
        new ReadOnlyCollection<RepositoryIncludeDesign>(includes);

    internal List<RepositoryIncludeDesign> IncludesList => includes;
}

public sealed class RepositoryQueryDesign
{
    private readonly List<LambdaExpression> predicates = new();
    private readonly List<RepositoryIncludeDesign> includes = new();
    private readonly List<RepositorySortDesign> sorts = new();

    internal RepositoryQueryDesign(string methodName)
    {
        MethodName = methodName;
        Cache = new RepositoryQueryCacheDesign();
    }

    public string MethodName { get; }

    public IReadOnlyList<LambdaExpression> Predicates => new ReadOnlyCollection<LambdaExpression>(predicates);

    public IReadOnlyList<RepositoryIncludeDesign> Includes => new ReadOnlyCollection<RepositoryIncludeDesign>(includes);

    public IReadOnlyList<RepositorySortDesign> Sorts => new ReadOnlyCollection<RepositorySortDesign>(sorts);

    public RepositoryProjectionDesign? Projection { get; internal set; }

    public RepositoryDynamicSortDesign? DynamicSort { get; internal set; }

    public RepositoryCursorDesign? Cursor { get; internal set; }

    public RepositoryGroupedQueryDesign? Group { get; internal set; }

    public RepositoryQueryResultKind ResultKind { get; internal set; } = RepositoryQueryResultKind.List;

    public RepositoryTrackingBehavior TrackingBehavior { get; internal set; }

    public RepositoryQuerySplittingBehavior SplittingBehavior { get; internal set; }

    public bool IgnoreQueryFilters { get; internal set; }

    public string? QueryTag { get; internal set; }

    public RepositoryQueryCacheDesign Cache { get; }

    internal List<LambdaExpression> PredicatesList => predicates;

    internal List<RepositoryIncludeDesign> IncludesList => includes;

    internal List<RepositorySortDesign> SortsList => sorts;
}

public sealed class RepositoryQueryCacheDesign
{
    private readonly HashSet<Type> dependentEntityTypes = new();

    public bool Enabled { get; internal set; }

    public TimeSpan? AbsoluteExpirationRelativeToNow { get; internal set; }

    public TimeSpan? SlidingExpiration { get; internal set; }

    public IReadOnlyCollection<Type> DependentEntityTypes => dependentEntityTypes;

    internal HashSet<Type> DependentEntityTypesSet => dependentEntityTypes;
}

public sealed class RepositoryGroupedQueryDesign
{
    internal RepositoryGroupedQueryDesign(
        Type resultType,
        LambdaExpression keySelector,
        LambdaExpression projection)
    {
        ResultType = resultType;
        KeySelector = keySelector;
        Projection = projection;
    }

    public Type ResultType { get; }

    public LambdaExpression KeySelector { get; }

    public LambdaExpression Projection { get; }
}

public sealed class RepositoryDynamicSortDesign
{
    internal RepositoryDynamicSortDesign(
        string fieldNameParameter,
        string descendingParameter,
        IReadOnlyList<string> propertyNames)
    {
        FieldNameParameter = fieldNameParameter;
        DescendingParameter = descendingParameter;
        PropertyNames = propertyNames;
    }

    public string FieldNameParameter { get; }

    public string DescendingParameter { get; }

    public IReadOnlyList<string> PropertyNames { get; }
}

public sealed class RepositoryCursorDesign
{
    internal RepositoryCursorDesign(
        Type cursorType,
        LambdaExpression seekPredicate,
        LambdaExpression cursorSelector)
    {
        CursorType = cursorType;
        SeekPredicate = seekPredicate;
        CursorSelector = cursorSelector;
    }

    public Type CursorType { get; }

    public LambdaExpression SeekPredicate { get; }

    public LambdaExpression CursorSelector { get; }
}

public enum RepositoryCommandKind
{
    Unspecified,
    Delete,
    Update
}

public sealed class RepositoryCommandDesign
{
    private readonly List<LambdaExpression> predicates = new();
    private readonly List<RepositoryUpdateDesign> updates = new();

    internal RepositoryCommandDesign(string methodName)
    {
        MethodName = methodName;
    }

    public string MethodName { get; }

    public RepositoryCommandKind Kind { get; internal set; }

    public IReadOnlyList<LambdaExpression> Predicates => new ReadOnlyCollection<LambdaExpression>(predicates);

    public IReadOnlyList<RepositoryUpdateDesign> Updates => new ReadOnlyCollection<RepositoryUpdateDesign>(updates);

    internal List<LambdaExpression> PredicatesList => predicates;

    internal List<RepositoryUpdateDesign> UpdatesList => updates;
}

public sealed class RepositoryUpdateDesign
{
    internal RepositoryUpdateDesign(LambdaExpression property, LambdaExpression value)
    {
        Property = property;
        Value = value;
    }

    public LambdaExpression Property { get; }

    public LambdaExpression Value { get; }
}

public sealed class RepositoryQueryFragmentDesign
{
    internal RepositoryQueryFragmentDesign(string name, LambdaExpression predicate)
    {
        Name = name;
        Predicate = predicate;
    }

    public string Name { get; }

    public LambdaExpression Predicate { get; }
}

public sealed class RepositoryFilterDesign
{
    internal RepositoryFilterDesign(string methodName, LambdaExpression predicate)
    {
        MethodName = methodName;
        Predicate = predicate;
    }

    public string MethodName { get; }

    public LambdaExpression Predicate { get; }

    public bool IsUnique { get; internal set; }
}

public sealed class RepositorySortDesign
{
    internal RepositorySortDesign(LambdaExpression keySelector)
    {
        KeySelector = keySelector;
    }

    public LambdaExpression KeySelector { get; }

    public bool IsDescending { get; internal set; }
}

public sealed class RepositoryIncludeDesign
{
    internal RepositoryIncludeDesign(LambdaExpression navigation)
    {
        Navigation = navigation;
    }

    public LambdaExpression Navigation { get; }
}

public sealed class RepositorySortableFieldDesign
{
    internal RepositorySortableFieldDesign(string propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; }
}