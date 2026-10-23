namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Opts this fixture store into portable constant insert defaults from the final EF model.</summary>
    /// <remarks>Requires a preceding store and automatic commands. Explicit generated-value factories
    /// take precedence and run first. Only constant, insert-only ordinary scalars are reused; SQL defaults,
    /// identity, computed/update values and provider-specific behavior still need explicit adapters.
    /// Model sentinels determine whether a supplied value is unset. Replacing the store clears this option.</remarks>
    public RepositoryEntityBuilder<TEntity> UseInMemoryModelDefaults(bool enabled = true)
    {
        RequireGeneratedValueStore().ModelDefaultsEnabled = enabled;
        return this;
    }
}
