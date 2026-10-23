using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Binds one typed input to filtered includes and their continuations on a named query.</summary>
public sealed class RepositoryLoadBuilder<TEntity, TRequest> where TEntity : class
{
    private readonly List<RepositoryIncludeDesign> includes;
    internal RepositoryLoadBuilder(List<RepositoryIncludeDesign> includes) => this.includes = includes;

    public IRepositoryIncludeChain<TEntity, TRequest, TNavigation> Include<TNavigation>(Expression<Func<TEntity, TNavigation>> navigation)
    {
        if (navigation is null)
        {
            throw new ArgumentNullException(nameof(navigation));
        }

        var design = new RepositoryIncludeDesign(navigation);
        includes.Add(design);
        return new RepositoryIncludeChain<TEntity, TRequest, TNavigation>(design, 0);
    }

    public IRepositoryIncludeChain<TEntity, TRequest, TNavigation> Include<TNavigation>(Expression<Func<TEntity, TRequest, TNavigation>> navigation)
    {
        if (navigation is null)
        {
            throw new ArgumentNullException(nameof(navigation));
        }

        var design = new RepositoryIncludeDesign(navigation);
        includes.Add(design);
        return new RepositoryIncludeChain<TEntity, TRequest, TNavigation>(design, 0);
    }
}
