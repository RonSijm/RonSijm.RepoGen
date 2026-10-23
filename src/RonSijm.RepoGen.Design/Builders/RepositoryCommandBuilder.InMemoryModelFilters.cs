namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Opts this command's fixture implementation into the final model's portable scalar filter.</summary>
    /// <remarks>Off by default, independent of entity read settings. Filters saved-row selection and
    /// fresh scalar/DTO ReturnsSaved reads, not inserts or the shared Save itself. includeNavigations
    /// additionally permits filtered ordinary collections/locally guarded optional references and
    /// explicit CLR many-to-many reads in fresh saved DTOs, with separate related-entity graph permission. Supports scalar
    /// tracked writes/batches, scalar immediate Update/Delete counts/returning and scalar atomic insertion/upsert.
    /// Atomic conflicts/insertion are unfiltered; upserts recheck the model filter on updated result reads.
    /// Bounded child/link reconciliation additionally needs SimulateCollectionWrites(includeModelQueryFilters: true).
    /// Broader graph persistence, unbound captured contexts and provider semantics still require overrides.
    /// Exact context scalars can separately use HasInMemoryModelFilterContext without changing selection/save timing.</remarks>
    public RepositoryCommandBuilder<TEntity> UseInMemoryModelQueryFilter(bool enabled = true, bool includeNavigations = false)
    {
        design.InMemoryModelQueryFilterEnabled = enabled;
        design.InMemoryModelQueryFilterNavigationsEnabled = enabled && includeNavigations;
        return this;
    }
}
