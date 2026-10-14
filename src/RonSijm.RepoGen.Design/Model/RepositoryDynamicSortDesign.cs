namespace RonSijm.RepoGen.Design;

public sealed class RepositoryDynamicSortDesign
{
    internal RepositoryDynamicSortDesign(
        string fieldNameParameter,
        string descendingParameter,
        IReadOnlyList<string> propertyNames)
    {
        FieldNameParameter = fieldNameParameter;
        DescendingParameter = descendingParameter;
        PropertyNames = propertyNames;
    }

    public string FieldNameParameter { get; }

    public string DescendingParameter { get; }

    public IReadOnlyList<string> PropertyNames { get; }
}
