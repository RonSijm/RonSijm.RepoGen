namespace RonSijm.RepoGen.Design;

public sealed class RepositorySortBuilder
{
    private readonly RepositorySortDesign design;

    internal RepositorySortBuilder(RepositorySortDesign design)
    {
        this.design = design;
    }

    public RepositorySortBuilder Descending(bool descending = true)
    {
        design.IsDescending = descending;
        return this;
    }
}
