namespace RonSijm.RepoGen.Design;

/// <summary>A domain-neutral configuration module, applied directly or through explicit assembly discovery.</summary>
public interface IRepositoryModelConfiguration
{
    void Configure(RepositoryModelBuilder builder);
}
