namespace RonSijm.RepoGen.Design;

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
