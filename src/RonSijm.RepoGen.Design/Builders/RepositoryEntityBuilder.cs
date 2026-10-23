using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity>
    where TEntity : class
{
    private readonly RepositoryEntityDesign design;

    internal RepositoryEntityBuilder(RepositoryEntityDesign design)
    {
        this.design = design;
    }

    /// <summary>Overrides the global SQL-command timeout for this entity's named closed operations. Null uses the context setting.</summary>
    public RepositoryEntityBuilder<TEntity> UseCommandTimeout(int? seconds)
    {
        design.CommandTimeout = new RepositoryCommandTimeoutDesign(seconds);
        return this;
    }

    /// <summary>Removes this entity's timeout override and inherits model defaults again.</summary>
    public RepositoryEntityBuilder<TEntity> InheritCommandTimeout()
    {
        design.CommandTimeout = null;
        return this;
    }

    /// <summary>Overrides the global review guard for this entity. Null restores inheritance.</summary>
    public RepositoryEntityBuilder<TEntity> RequireNavigationVisibilityReview(bool? enabled = true)
    {
        design.NavigationVisibility.Required = enabled;
        return this;
    }

    /// <summary>Records a deliberate review of one target entity's policy for this entity's configured operations.</summary>
    public RepositoryEntityBuilder<TEntity> ReviewNavigationVisibility<TTarget>(string policyName, string reason) where TTarget : class
    {
        design.NavigationVisibility.Review<TTarget>(policyName, reason);
        return this;
    }

    /// <summary>Excludes this entire entity's repository and selectors when false.</summary>
    public bool RepositoryEnabled
    {
        get => design.RepositoryEnabled;
        set => design.RepositoryEnabled = value;
    }

    /// <summary>Controls the read port in closed-operation or CQRS-selector mode.</summary>
    public bool ReadEnabled
    {
        get => design.ReadEnabled;
        set => design.ReadEnabled = value;
    }

    /// <summary>Controls the write port in closed-operation or CQRS-selector mode.</summary>
    public bool WriteEnabled
    {
        get => design.WriteEnabled;
        set => design.WriteEnabled = value;
    }

    /// <summary>Binds named queries to an existing public application interface in closed mode.</summary>
    public RepositoryEntityBuilder<TEntity> BindReadPort<TPort>() => BindPort(typeof(TPort), design.ReadPortsList);

    /// <summary>Binds named commands to an existing public application interface in closed mode.</summary>
    public RepositoryEntityBuilder<TEntity> BindWritePort<TPort>() => BindPort(typeof(TPort), design.WritePortsList);

    private RepositoryEntityBuilder<TEntity> BindPort(Type port, List<Type> ports)
    {
        if (!port.IsInterface || !port.IsVisible || port.ContainsGenericParameters)
        {
            throw new ArgumentException("RGD1102: A port must be a public, closed interface.", nameof(port));
        }

        if (!ports.Contains(port))
        {
            ports.Add(port);
        }

        return this;
    }

    /// <summary>
    /// Gets or sets whether consumers may supply ad-hoc projection expressions for this entity.
    /// Designer-defined projections and named queries remain available when this is disabled.
    /// </summary>
    public bool ProjectionEnabled
    {
        get => design.ProjectionEnabled;
        set => design.ProjectionEnabled = value;
    }

    public RepositoryAccessorBuilder HasAccessor<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        var propertyInfo = GetProperty(property);
        var accessor = design.AccessorsList.FirstOrDefault(item => item.PropertyName == propertyInfo.Name);
        if (accessor is null)
        {
            accessor = new RepositoryAccessorDesign(propertyInfo.Name, propertyInfo.PropertyType);
            design.AccessorsList.Add(accessor);
        }

        accessor.UsesMappedPropertyName = false;
        return new RepositoryAccessorBuilder(accessor);
    }

    public RepositoryEntityBuilder<TEntity> Exclude<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        design.ExcludedPropertiesSet.Add(GetProperty(property).Name);
        return this;
    }

    public RepositoryProjectionBuilder<TEntity> ProjectTo<TProjection>(Expression<Func<TEntity, TProjection>> projection)
        => AddProjection(projection, methodName: null, useExactMethodName: false);

    public RepositoryProjectionBuilder<TEntity> HasProjection<TProjection>(
        Expression<Func<TEntity, TProjection>> projection)
        => AddProjection(
            projection,
            "ProjectTo" + GetTypeNameWithoutGenericArity(typeof(TProjection)),
            useExactMethodName: true);

    public RepositoryProjectionBuilder<TEntity> HasProjection<TProjection>(RepositoryTypeAdapter<TEntity, TProjection> adapter)
    {
        if (adapter is null)
        {
            throw new ArgumentNullException(nameof(adapter));
        }

        return HasProjection(entity => adapter.ToContract(entity));
    }

    public RepositoryProjectionBuilder<TEntity> HasProjection<TProjection>(
        string methodName,
        Expression<Func<TEntity, TProjection>> projection)
        => AddProjection(projection, RequireMethodName(methodName), useExactMethodName: true);

    private RepositoryProjectionBuilder<TEntity> AddProjection<TProjection>(
        Expression<Func<TEntity, TProjection>> projection,
        string? methodName,
        bool useExactMethodName)
    {
        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        var projectionDesign = new RepositoryProjectionDesign(
            typeof(TProjection),
            projection,
            methodName,
            useExactMethodName);
        design.ProjectionsList.Add(projectionDesign);
        return new RepositoryProjectionBuilder<TEntity>(projectionDesign);
    }

    private static string GetTypeNameWithoutGenericArity(Type type)
    {
        var name = type.Name;
        var genericMarker = name.IndexOf('`');
        return genericMarker < 0 ? name : name.Substring(0, genericMarker);
    }

    public RepositoryQueryBuilder<TEntity> HasQuery(string methodName)
    {
        var query = new RepositoryQueryDesign(RequireMethodName(methodName));
        design.QueriesList.Add(query);
        return new RepositoryQueryBuilder<TEntity>(query);
    }

    /// <summary>A required root visibility predicate, unless a named designer operation explicitly bypasses it.</summary>
    public RepositoryEntityBuilder<TEntity> HasVisibilityPolicy(string name, Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        design.VisibilityPoliciesList.Add(new RepositoryQueryFragmentDesign(RequireMethodName(name), predicate));
        return this;
    }

    public RepositoryCommandBuilder<TEntity> HasCommand(string methodName)
    {
        var command = new RepositoryCommandDesign(RequireMethodName(methodName));
        design.CommandsList.Add(command);
        return new RepositoryCommandBuilder<TEntity>(command);
    }

    /// <summary>Removes all configurations with the given query name, e.g. after applying a module.</summary>
    public RepositoryEntityBuilder<TEntity> ExcludeQuery(string methodName)
    {
        var name = RequireMethodName(methodName);
        design.QueriesList.RemoveAll(query => string.Equals(query.MethodName, name, StringComparison.Ordinal));
        return this;
    }

    public RepositoryEntityBuilder<TEntity> ExcludeCommand(string methodName)
    {
        var name = RequireMethodName(methodName);
        design.CommandsList.RemoveAll(command => string.Equals(command.MethodName, name, StringComparison.Ordinal));
        return this;
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

    public RepositoryQueryFragment<TEntity, TRequest> HasFragment<TRequest>(string name, Expression<Func<TEntity, TRequest, bool>> predicate)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var fragment = new RepositoryQueryFragment<TEntity, TRequest>(RequireMethodName(name), predicate);
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

    public RepositoryEntityBuilder<TEntity> Load(Action<RepositoryLoadBuilder<TEntity>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(new RepositoryLoadBuilder<TEntity>(design.IncludesList));
        return this;
    }

    public RepositoryLoadProfile<TEntity> HasLoadProfile(string name, Action<RepositoryLoadBuilder<TEntity>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var profile = AddLoadProfile(name);
        configure(new RepositoryLoadBuilder<TEntity>(profile.IncludesList));
        return new RepositoryLoadProfile<TEntity>(profile);
    }

    public RepositoryLoadProfile<TEntity> HasLoadProfile<TRequest>(string name, Action<RepositoryLoadBuilder<TEntity, TRequest>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var profile = AddLoadProfile(name);
        configure(new RepositoryLoadBuilder<TEntity, TRequest>(profile.IncludesList));
        return new RepositoryLoadProfile<TEntity>(profile);
    }

    private RepositoryLoadProfileDesign AddLoadProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A load profile name is required.", nameof(name));
        }

        var profile = new RepositoryLoadProfileDesign(typeof(TEntity), name.Trim());
        design.LoadProfilesList.Add(profile);
        return profile;
    }

    public RepositoryEntityBuilder<TEntity> UseLoadProfile(string name)
    {
        design.Loading.Use(name);
        return this;
    }

    public RepositoryEntityBuilder<TEntity> UseLoadProfile(RepositoryLoadProfile<TEntity> profile)
    {
        design.Loading.Use(profile);
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
