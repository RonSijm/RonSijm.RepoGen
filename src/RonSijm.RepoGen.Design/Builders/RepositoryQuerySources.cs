namespace RonSijm.RepoGen.Design;

/// <summary>Typed query roots for a designer expression. Every set uses the operation's one context.</summary>
/// <remarks>This is a design-time placeholder, not a runtime service. RepoGen inspects and emits the expression without invoking it.</remarks>
public sealed class RepositoryQuerySources
{
    private RepositoryQuerySources() { }

#pragma warning disable CA1822 // The instance parameter identifies the operation's typed roots in the expression tree.
    public IQueryable<TEntity> Set<TEntity>() where TEntity : class =>
        throw new InvalidOperationException("RepositoryQuerySources.Set is only valid inside a designer From expression.");
#pragma warning restore CA1822
}
