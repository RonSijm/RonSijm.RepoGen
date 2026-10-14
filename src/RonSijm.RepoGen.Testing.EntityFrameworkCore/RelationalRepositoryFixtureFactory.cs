using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Testing.EntityFrameworkCore;

/// <summary>Creates an explicitly configured fixture without selecting a provider or inferring database ownership.</summary>
public static class RelationalRepositoryFixture
{
    public static Task<RelationalRepositoryFixture<TContext>> CreateAsync<TContext>(
        Action<RelationalRepositoryFixtureOptions<TContext>> configure, CancellationToken cancellationToken = default)
        where TContext : DbContext => RelationalRepositoryFixture<TContext>.CreateAsync(configure, cancellationToken);
}
