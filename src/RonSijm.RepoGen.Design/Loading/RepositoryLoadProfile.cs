namespace RonSijm.RepoGen.Design;

public sealed class RepositoryLoadProfile<TEntity> where TEntity : class
{
    internal RepositoryLoadProfile(RepositoryLoadProfileDesign design) => Design = design;
    internal RepositoryLoadProfileDesign Design { get; }
    public string Name => Design.Name;
}
