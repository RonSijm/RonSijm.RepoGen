namespace RonSijm.RepoGen.Design;

internal sealed class RepositoryIncludeChain<TEntity, TRequest, TNavigation> : IRepositoryIncludeChain<TEntity, TRequest, TNavigation>, IRepositoryIncludeChainState
    where TEntity : class
{
    internal RepositoryIncludeChain(RepositoryIncludeDesign configuration, int depth)
    {
        Configuration = configuration;
        Depth = depth;
    }

    public RepositoryIncludeDesign Configuration { get; }
    public int Depth { get; }
}
