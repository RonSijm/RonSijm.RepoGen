using System.Collections.ObjectModel;

namespace RonSijm.RepoGen.Design;

/// <summary>A typed, design-time include chain. Collection continuations select an element, not the collection.</summary>
public interface IRepositoryIncludeChain<TEntity, out TNavigation> where TEntity : class
{
    RepositoryIncludeDesign Configuration { get; }
}
