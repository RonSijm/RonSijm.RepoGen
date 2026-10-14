namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Opts this command's fixture implementation into the final model's portable scalar filter.</summary>
    /// <remarks>Off by default, independent of entity read settings. Filters saved-row selection and
    /// fresh scalar/DTO ReturnsSaved reads, not inserts or the shared Save itself. includeNavigations
    /// additionally permits filtered ordinary collections/locally guarded optional references in
    /// fresh saved DTOs, with separate related-entity graph permission. Supports scalar
    /// tracked writes/batches and scalar immediate Update/Delete counts/returning; graph mutations,
    /// atomic insert/upsert, captured contexts and provider semantics still require overrides.</remarks>
    public RepositoryCommandBuilder<TEntity> UseInMemoryModelQueryFilter(bool enabled = true, bool includeNavigations = false)
    {
        design.InMemoryModelQueryFilterEnabled = enabled;
        design.InMemoryModelQueryFilterNavigationsEnabled = enabled && includeNavigations;
        return this;
    }
}
