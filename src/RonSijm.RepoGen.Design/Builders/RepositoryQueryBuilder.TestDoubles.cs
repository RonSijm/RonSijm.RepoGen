namespace RonSijm.RepoGen.Design;

public partial class RepositoryQueryBuilder<TEntity> where TEntity : class
{
    /// <summary>Requires an explicit test script instead of automatic fixture execution for this read.</summary>
    /// <remarks>Does not alter the production query. Null removes the acknowledgement and rechecks portability.</remarks>
    public RepositoryQueryBuilder<TEntity> RequireTestOverride(string? reason)
    {
        if (reason is not null && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A nonempty test override reason is required, or null to recheck portability.", nameof(reason));
        design.TestOverrideReason = reason;
        return this;
    }
}
