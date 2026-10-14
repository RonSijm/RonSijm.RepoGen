using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryProjectionBuilder<TEntity> : RepositoryProjectionBuilder
    where TEntity : class
{
    internal RepositoryProjectionBuilder(RepositoryProjectionDesign design)
        : base(design) { }

    public new RepositoryProjectionBuilder<TEntity> RequireNavigationVisibilityReview(bool? enabled = true)
    {
        base.RequireNavigationVisibilityReview(enabled);
        return this;
    }

    public new RepositoryProjectionBuilder<TEntity> ReviewNavigationVisibility<TTarget>(string policyName, string reason) where TTarget : class
    {
        base.ReviewNavigationVisibility<TTarget>(policyName, reason);
        return this;
    }

    public new RepositoryProjectionBuilder<TEntity> HasMethodName(string methodName)
    {
        base.HasMethodName(methodName);
        return this;
    }

    public new RepositoryProjectionBuilder<TEntity> ResultNullability(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        base.ResultNullability(configure);
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

    public RepositoryProjectionBuilder<TEntity> Load(Action<RepositoryLoadBuilder<TEntity>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(new RepositoryLoadBuilder<TEntity>(Design.IncludesList));
        return this;
    }

    public RepositoryProjectionBuilder<TEntity> UseLoadProfile(string name)
    {
        Design.Loading.Use(name);
        return this;
    }

    public RepositoryProjectionBuilder<TEntity> UseLoadProfile(RepositoryLoadProfile<TEntity> profile)
    {
        Design.Loading.Use(profile);
        return this;
    }

    /// <summary>Controls automatic root-navigation loading inferred from this projection. Explicit loading remains active.</summary>
    public RepositoryProjectionBuilder<TEntity> AutoIncludeNavigations(bool enabled = true)
    {
        Design.AutoIncludeNavigations = enabled;
        return this;
    }
}
