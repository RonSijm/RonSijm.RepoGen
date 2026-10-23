namespace RonSijm.RepoGen.Design;

public sealed class RepositoryFilterBuilder
{
    private readonly RepositoryFilterDesign design;

    internal RepositoryFilterBuilder(RepositoryFilterDesign design)
    {
        this.design = design;
    }

    public RepositoryFilterBuilder IsUnique(bool unique = true)
    {
        design.IsUnique = unique;
        return this;
    }
}
