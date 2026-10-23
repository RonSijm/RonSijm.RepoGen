namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Captures approved mapped scalars in native ReturnsMany or atomic Returns results, including fields, indexers and shadows.</summary>
    /// <remarks>Requires an IRepositoryReturningMappedStateWriter or IRepositoryAtomicMappedStateWriter. Values are read from detached scalar buffers,
    /// not entity getter behavior. Keys/setter targets remain ordinary CLR properties; no fixture or graph state is inferred.
    /// Disabling this permission does not clear a separately configured IncludeShadowProperties permission.</remarks>
    public RepositoryCommandBuilder<TEntity> IncludeMappedProperties(bool enabled = true)
    {
        design.IncludeReturningMappedProperties = enabled;
        return this;
    }

    /// <summary>Explicitly permits shadow scalars in native ReturnsMany or atomic Returns results.</summary>
    /// <remarks>Requires the corresponding returning-state-capable or atomic-state-capable writer. Keys and assignment targets remain CLR properties;
    /// this does not enable tracked/fixture shadow state, graph loading or shadow-property assignments.</remarks>
    public RepositoryCommandBuilder<TEntity> IncludeShadowProperties(bool enabled = true)
    {
        design.IncludeReturningShadowProperties = enabled;
        return this;
    }
}
