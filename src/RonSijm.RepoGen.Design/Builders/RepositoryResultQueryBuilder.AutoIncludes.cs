namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryResultQueryBuilder<TResult>
{
    /// <summary>Disables user-configured EF model AutoInclude loading on every root of this named query.</summary>
    /// <remarks>Applies before projection/result stages, retaining explicit loading and query filters.
    /// Owned loading and tracking fix-up are not suppressed. Registered providers own their root
    /// policies and reject this option. Pass false to restore model-configured loading.</remarks>
    public RepositoryResultQueryBuilder<TResult> IgnoreAutoIncludes(bool ignore = true)
    {
        RequireCurrentStage();
        design.IgnoreAutoIncludes = ignore;
        return this;
    }
}
