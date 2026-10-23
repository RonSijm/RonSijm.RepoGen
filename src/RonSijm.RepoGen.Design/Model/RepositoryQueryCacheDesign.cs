namespace RonSijm.RepoGen.Design;

public sealed class RepositoryQueryCacheDesign
{
    private readonly HashSet<Type> dependentEntityTypes = new();

    public bool Enabled { get; internal set; }

    public TimeSpan? AbsoluteExpirationRelativeToNow { get; internal set; }

    public TimeSpan? SlidingExpiration { get; internal set; }

    public IReadOnlyCollection<Type> DependentEntityTypes => dependentEntityTypes;

    internal HashSet<Type> DependentEntityTypesSet => dependentEntityTypes;
}
