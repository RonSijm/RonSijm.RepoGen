namespace RonSijm.RepoGen.Design;

internal interface IRepositoryIncludeChainState
{
    RepositoryIncludeDesign Configuration { get; }
    int Depth { get; }
}
