using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> : IRepositoryOperationBuilder
    where TEntity : class
{
    /// <summary>Overrides entity/model SQL-command timeouts for this named closed command. Null uses the context setting.</summary>
    public RepositoryCommandBuilder<TEntity> UseCommandTimeout(int? seconds)
    {
        design.CommandTimeout = new RepositoryCommandTimeoutDesign(seconds);
        return this;
    }

    /// <summary>Removes this named command's override and inherits the entity/model setting again.</summary>
    public RepositoryCommandBuilder<TEntity> InheritCommandTimeout()
    {
        design.CommandTimeout = null;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> AllowNullInput<TInput>(string parameterName) where TInput : class?
    {
        design.InputNullability = design.InputNullability.Concat(new[] { new RepositoryInputNullabilityDesign(parameterName, typeof(TInput)) }).ToArray();
        return this;
    }

    public RepositoryCommandBuilder<TEntity> InputNullability<TInput>(string parameterName, Action<RepositoryTypeNullabilityBuilder> configure)
    {
        design.InputNullability = design.InputNullability.Concat(new[]
        {
            new RepositoryInputNullabilityDesign(parameterName,
                RepositoryTypeNullabilityBuilder.Create(typeof(TInput), configure))
        }).ToArray();
        return this;
    }

    /// <summary>Annotates the configured Returns value or ReturnsMany item, not its wrapper or Task.</summary>
    public RepositoryCommandBuilder<TEntity> ResultNullability<TValue>(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (design.ResultNullability is not null)
        {
            throw new InvalidOperationException($"RGN0005: Command '{design.MethodName}' configures result nullability more than once.");
        }

        design.ResultNullability = RepositoryTypeNullabilityBuilder.Create(typeof(TValue), configure);
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Validate<TRequest>(Expression<Func<TRequest, bool>> predicate, string message)
    {
        design.ValidationsList.Add(new RepositoryInputValidationDesign(predicate, message));
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Use<TRequest>(RepositoryQueryFragment<TEntity, TRequest> fragment)
    {
        design.PredicatesList.Add((fragment ?? throw new ArgumentNullException(nameof(fragment))).Design.Predicate);
        return this;
    }

    public RepositoryCommandBuilder<TEntity> UseContextKey<TRequest, TKey>(Expression<Func<TRequest, TKey>> key)
    {
        design.ContextKey = key ?? throw new ArgumentNullException(nameof(key));
        return this;
    }

    public RepositoryCommandBuilder<TEntity> BypassVisibility(params string[] names)
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

    public RepositoryCommandBuilder<TEntity> RequireNavigationVisibilityReview(bool? enabled = true)
    {
        design.NavigationVisibility.Required = enabled;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> ReviewNavigationVisibility<TTarget>(string policyName, string reason) where TTarget : class
    {
        design.NavigationVisibility.Review<TTarget>(policyName, reason);
        return this;
    }

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

    /// <summary>Maps an application request into a new entity. Choose Save() or Stage() explicitly.</summary>
    public RepositoryCommandBuilder<TEntity> Create<TRequest>(Expression<Func<TRequest, TEntity>> mapping)
    {
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        design.Kind = RepositoryCommandKind.Create;
        design.CreateMapping = mapping;
        return this;
    }

    /// <summary>Uses a declared primary/unique conflict key. Executes immediately; never fetches before inserting.</summary>
    public RepositoryCommandBuilder<TEntity> InsertIfAbsent<TKey>(Expression<Func<TEntity, TKey>> conflictKey)
    {
        design.Kind = RepositoryCommandKind.InsertIfAbsent;
        design.ConflictKey = conflictKey ?? throw new ArgumentNullException(nameof(conflictKey));
        return this;
    }

    public RepositoryCommandBuilder<TEntity> InsertOrUpdate<TKey>(Expression<Func<TEntity, TKey>> conflictKey, Action<RepositoryUpdateBuilder<TEntity>> configureUpdate)
    {
        if (configureUpdate is null)
        {
            throw new ArgumentNullException(nameof(configureUpdate));
        }

        design.Kind = RepositoryCommandKind.InsertOrUpdate;
        design.ConflictKey = conflictKey ?? throw new ArgumentNullException(nameof(conflictKey));
        configureUpdate(new RepositoryUpdateBuilder<TEntity>(design));
        return this;
    }

    /// <summary>Changes only the configured properties of one tracked row.</summary>
    public RepositoryCommandBuilder<TEntity> Patch(Action<RepositoryUpdateBuilder<TEntity>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        design.Kind = RepositoryCommandKind.Patch;
        configure(new RepositoryUpdateBuilder<TEntity>(design));
        return this;
    }

    public RepositoryCommandBuilder<TEntity> TrackedDelete()
    {
        design.Kind = RepositoryCommandKind.TrackedDelete;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Save()
    {
        design.SaveChanges = true;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Stage()
    {
        design.SaveChanges = false;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> ReloadAfterSave(bool enabled = true)
    {
        design.ReloadAfterSave = enabled;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Returns<TResult>(Expression<Func<TEntity, TResult>> projection)
    {
        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        design.ResultProjection = projection;
        design.ReturnsMany = false;
        design.ResultFromSaved = false;
        design.SavedResultMaximumRows = null;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> Returns<TResult, TRequest>(Expression<Func<TEntity, TRequest, TResult>> projection)
    {
        if (projection is null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        design.ResultProjection = projection;
        design.ReturnsMany = false;
        design.ResultFromSaved = false;
        design.SavedResultMaximumRows = null;
        return this;
    }

    /// <summary>Returns each directly affected row of an immediate Update/Delete using the selected native writer.</summary>
    /// <remarks>Maps returned scalar columns to application values. No navigation loading, staging, or tracker synchronization.</remarks>
    public RepositoryCommandBuilder<TEntity> ReturnsMany<TResult>(Expression<Func<TEntity, TResult>> projection)
    {
        design.ResultProjection = projection ?? throw new ArgumentNullException(nameof(projection));
        design.ReturnsMany = true;
        design.ResultFromSaved = false;
        design.SavedResultMaximumRows = null;
        return this;
    }

    public RepositoryCommandBuilder<TEntity> ReturnsMany<TResult, TRequest>(Expression<Func<TEntity, TRequest, TResult>> projection)
    {
        design.ResultProjection = projection ?? throw new ArgumentNullException(nameof(projection));
        design.ReturnsMany = true;
        design.ResultFromSaved = false;
        design.SavedResultMaximumRows = null;
        return this;
    }

    /// <summary>Explicitly permits a protected scalar assignment; EF's immutable/generated rules still apply.</summary>
    public RepositoryCommandBuilder<TEntity> AllowWrite<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        design.AllowedWritesList.Add(property);
        return this;
    }

    public RepositoryCommandBuilder<TEntity> ExpectVersion<TProperty, TRequest>(Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TRequest, TProperty>> expected)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        if (expected is null)
        {
            throw new ArgumentNullException(nameof(expected));
        }

        design.ExpectedVersionsList.Add(new RepositoryExpectedVersionDesign(property, expected));
        return this;
    }

    /// <summary>Returns NotMatched for zero set-based rows; does not guess absence versus conflict.</summary>
    public RepositoryCommandBuilder<TEntity> WithOutcome(bool enabled = true)
    {
        design.WithOutcome = enabled;
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
