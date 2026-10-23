namespace RonSijm.RepoGen.Design;

public sealed class RepositoryLoadProfileDesign
{
    private readonly List<RepositoryIncludeDesign> includes = new();

    internal RepositoryLoadProfileDesign(Type entityType, string name)
    {
        EntityType = entityType;
        Name = name;
    }

    public Type EntityType { get; }
    public string Name { get; }
    public IReadOnlyList<RepositoryIncludeDesign> Includes => includes.AsReadOnly();
    internal List<RepositoryIncludeDesign> IncludesList => includes;
}
