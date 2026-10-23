namespace RonSijm.RepoGen.Design;

/// <summary>Configures one entity without requiring a monolithic designer class.</summary>
/// <remarks>Non-null property values apply before the optional Configure method. After Configure,
/// public instance methods returning IRepositoryOperationBuilder and taking the same
/// RepositoryEntityBuilder are invoked base-first, in metadata declaration order within each declaring type.
/// Keep ordinary helper methods private.</remarks>
public interface IRepositoryEntityConfiguration<TEntity> where TEntity : class
{
    /// <summary>Overrides whether this entity has a repository. Null preserves its existing setting.</summary>
    bool? RepositoryEnabled => null;

    /// <summary>Overrides the read capability. Null preserves its existing setting.</summary>
    bool? ReadEnabled => null;

    /// <summary>Overrides the write capability. Null preserves its existing setting.</summary>
    bool? WriteEnabled => null;

    /// <summary>Overrides ad-hoc projection support. Null preserves its existing setting.</summary>
    bool? ProjectionEnabled => null;

    /// <summary>Optionally configures shared projections, fragments, loading profiles or other entity details.</summary>
    void Configure(RepositoryEntityBuilder<TEntity> entity) { }
}
