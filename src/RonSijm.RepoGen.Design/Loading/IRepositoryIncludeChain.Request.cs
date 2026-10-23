namespace RonSijm.RepoGen.Design;

public interface IRepositoryIncludeChain<TEntity, TRequest, out TNavigation> : IRepositoryIncludeChain<TEntity, TNavigation>
    where TEntity : class
{ }
