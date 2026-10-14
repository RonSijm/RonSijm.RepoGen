namespace RonSijm.RepoGen.Design;

/// <summary>A domain-neutral module of repository configuration, applied explicitly by the designer.</summary>
public interface IRepositoryModelConfiguration
{
    void Configure(RepositoryModelBuilder builder);
}
