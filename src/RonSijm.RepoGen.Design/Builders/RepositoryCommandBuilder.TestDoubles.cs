namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Opts this command's generated fixture implementation into registered scalar-call translations.</summary>
    /// <remarks>Requires the automatic fixture command backend. Production expressions, write permissions,
    /// storage/value adapters and provider guarantees are unchanged. Disabled by default, independently of reads.</remarks>
    public RepositoryCommandBuilder<TEntity> UseInMemoryTranslations(bool enabled = true)
    {
        design.InMemoryTranslations = enabled;
        return this;
    }

    /// <summary>Opts designed bounded leaf-child reconciliation into fixture-only Stage/Save execution.</summary>
    /// <remarks>Requires selected parent/child stores and explicit scalar write maps for public data shapes.
    /// Ownership is derived from mapped relationship keys. Entity graphs, SQL constraints, cascades,
    /// provider-generated behavior and navigation-dependent command results are not inferred.
    /// includeModelQueryFilters additionally permits portable scalar model filters on selected child/link
    /// datasets. Off by default and independent of read/saved-result permissions; parent filters still
    /// need UseInMemoryModelQueryFilter. Inserts and shared Save are not validated against query filters.</remarks>
    public RepositoryCommandBuilder<TEntity> SimulateCollectionWrites(bool enabled = true, bool includeModelQueryFilters = false)
    {
        design.InMemoryCollectionWrites = enabled;
        design.InMemoryCollectionModelQueryFilters = enabled && includeModelQueryFilters;
        return this;
    }

    /// <summary>Opts an immediate scalar Update/Delete or InsertIfAbsent/InsertOrUpdate command into fixture-only execution.</summary>
    /// <remarks>Uses saved rows without flushing or refreshing staged writes. SQL constraints, triggers,
    /// cascades, locking and provider race guarantees are not simulated. Unsupported shapes still require a script.</remarks>
    public RepositoryCommandBuilder<TEntity> SimulateImmediateWrites(bool enabled = true)
    {
        design.InMemoryImmediateWrites = enabled;
        return this;
    }

    /// <summary>Acknowledges that this test command needs an explicit typed script, not simulated persistence.</summary>
    public RepositoryCommandBuilder<TEntity> RequireTestOverride(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A test override reason is required.", nameof(reason));
        design.TestOverrideReason = reason.Trim();
        return this;
    }
}
