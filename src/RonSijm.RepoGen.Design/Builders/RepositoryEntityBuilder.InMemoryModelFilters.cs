namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryEntityBuilder<TEntity> where TEntity : class
{
    /// <summary>Opts reads into the final EF model's portable scalar query filter.</summary>
    /// <remarks>Off by default. Requires the in-memory query backend to execute. No filter is inferred
    /// when the model has none, and IgnoreQueryFilters bypasses it. Unbound context/captured state, shadow
    /// properties and inheritance still require an explicit test override. includeNavigations additionally
    /// permits this entity's filter before explicitly declared collection/optional-reference and
    /// explicit CLR many-to-many loading. Required ordinary references to filtered targets remain scripted.
    /// Exact context scalars can separately use HasInMemoryModelFilterContext with the operation's typed key.
    /// This does not enable filtered writes, alter production queries or promise SQL equivalence.</remarks>
    public RepositoryEntityBuilder<TEntity> UseInMemoryModelQueryFilter(bool enabled = true, bool includeNavigations = false)
    {
        design.InMemoryModelQueryFilterEnabled = enabled;
        design.InMemoryModelQueryFilterNavigationsEnabled = enabled && includeNavigations;
        return this;
    }
}
