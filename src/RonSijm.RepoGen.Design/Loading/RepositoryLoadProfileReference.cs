namespace RonSijm.RepoGen.Design;

internal sealed class RepositoryLoadProfileReference
{
    internal RepositoryLoadProfileReference(string name, RepositoryLoadProfileDesign? profile = null)
    {
        Name = name;
        Profile = profile;
    }

    internal string Name { get; }
    internal RepositoryLoadProfileDesign? Profile { get; }
}
