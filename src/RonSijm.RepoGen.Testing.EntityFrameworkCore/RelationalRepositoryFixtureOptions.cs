using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RonSijm.RepoGen.Testing.EntityFrameworkCore;

/// <summary>Explicit provider, application registrations and database ownership policy for a fixture.</summary>
/// <remarks>No database is created/deleted/reset unless its callback is supplied. Callbacks must not re-enter the fixture.
/// Register TContext as scoped; scopes own their resolved contexts and application services.</remarks>
public sealed class RelationalRepositoryFixtureOptions<TContext> where TContext : DbContext
{
    public Action<IServiceCollection>? ConfigureServices { get; set; }
    public Func<TContext, CancellationToken, Task>? InitializeDatabaseAsync { get; set; }
    public Func<TContext, CancellationToken, Task>? SeedAsync { get; set; }
    public Func<TContext, CancellationToken, Task>? ResetDatabaseAsync { get; set; }
}
