using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public static class RepositoryIncludeChainExtensions
{
    public static IRepositoryIncludeChain<TEntity, TNavigation> ThenInclude<TEntity, TPrevious, TNavigation>(
        this IRepositoryIncludeChain<TEntity, TPrevious> chain, Expression<Func<TPrevious, TNavigation>> navigation)
        where TEntity : class => Continue<TEntity, TNavigation>(chain, navigation);

    public static IRepositoryIncludeChain<TEntity, TNavigation> ThenInclude<TEntity, TPrevious, TNavigation>(
        this IRepositoryIncludeChain<TEntity, IEnumerable<TPrevious>> chain, Expression<Func<TPrevious, TNavigation>> navigation)
        where TEntity : class => Continue<TEntity, TNavigation>(chain, navigation);

    public static IRepositoryIncludeChain<TEntity, TRequest, TNavigation> ThenInclude<TEntity, TRequest, TPrevious, TNavigation>(
        this IRepositoryIncludeChain<TEntity, TRequest, TPrevious> chain, Expression<Func<TPrevious, TNavigation>> navigation)
        where TEntity : class => Continue<TEntity, TRequest, TNavigation>(chain, navigation);

    public static IRepositoryIncludeChain<TEntity, TRequest, TNavigation> ThenInclude<TEntity, TRequest, TPrevious, TNavigation>(
        this IRepositoryIncludeChain<TEntity, TRequest, IEnumerable<TPrevious>> chain, Expression<Func<TPrevious, TNavigation>> navigation)
        where TEntity : class => Continue<TEntity, TRequest, TNavigation>(chain, navigation);

    public static IRepositoryIncludeChain<TEntity, TRequest, TNavigation> ThenInclude<TEntity, TRequest, TPrevious, TNavigation>(
        this IRepositoryIncludeChain<TEntity, TRequest, TPrevious> chain, Expression<Func<TPrevious, TRequest, TNavigation>> navigation)
        where TEntity : class => Continue<TEntity, TRequest, TNavigation>(chain, navigation);

    public static IRepositoryIncludeChain<TEntity, TRequest, TNavigation> ThenInclude<TEntity, TRequest, TPrevious, TNavigation>(
        this IRepositoryIncludeChain<TEntity, TRequest, IEnumerable<TPrevious>> chain, Expression<Func<TPrevious, TRequest, TNavigation>> navigation)
        where TEntity : class => Continue<TEntity, TRequest, TNavigation>(chain, navigation);

    private static RepositoryIncludeChain<TEntity, TNavigation> Continue<TEntity, TNavigation>(object chain, LambdaExpression navigation)
        where TEntity : class
    {
        var design = Extend(chain, navigation);
        return new RepositoryIncludeChain<TEntity, TNavigation>(design, design.ThenIncludes.Count);
    }

    private static RepositoryIncludeChain<TEntity, TRequest, TNavigation> Continue<TEntity, TRequest, TNavigation>(object chain, LambdaExpression navigation)
        where TEntity : class
    {
        var design = Extend(chain, navigation);
        return new RepositoryIncludeChain<TEntity, TRequest, TNavigation>(design, design.ThenIncludes.Count);
    }

    private static RepositoryIncludeDesign Extend(object chain, LambdaExpression navigation)
    {
        if (chain is null)
        {
            throw new ArgumentNullException(nameof(chain));
        }

        if (navigation is null)
        {
            throw new ArgumentNullException(nameof(navigation));
        }

        if (chain is not IRepositoryIncludeChainState state)
        {
            throw new ArgumentException("Use a chain returned by a RepositoryLoadBuilder.", nameof(chain));
        }

        if (state.Depth != state.Configuration.ThenIncludes.Count)
        {
            throw new InvalidOperationException("This include chain has already been extended. Start another Include() to declare a separate branch.");
        }

        state.Configuration.ThenIncludesList.Add(navigation);
        return state.Configuration;
    }
}
