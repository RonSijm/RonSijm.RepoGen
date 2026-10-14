namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryQueryBuilder<TEntity> where TEntity : class
{
    /// <summary>Disables user-configured EF model AutoInclude loading for this whole named query.</summary>
    /// <remarks>Explicit includes, loading profiles and projection navigation inference remain enabled.
    /// This does not bypass query filters, suppress owned navigations, or undo tracking fix-up.
    /// Pass false to restore model-configured loading. No runtime selector method is exposed.</remarks>
    public RepositoryQueryBuilder<TEntity> IgnoreAutoIncludes(bool ignore = true)
    {
        design.IgnoreAutoIncludes = ignore;
        return this;
    }
}
