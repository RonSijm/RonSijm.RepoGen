namespace RonSijm.RepoGen.Design;

public sealed class RepositorySortableFieldDesign
{
    internal RepositorySortableFieldDesign(string propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; }
}
