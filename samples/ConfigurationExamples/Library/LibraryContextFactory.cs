using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Models.Library;

namespace RonSijm.RepoGen.ConfigurationExamples.Library;

internal sealed class LibraryContextFactory(DbContextOptions<LibraryContext> options) : IDbContextFactory<LibraryContext>
{
    public List<LibraryContext> Contexts { get; } = [];
    public LibraryContext CreateDbContext() => throw new InvalidOperationException("This example expects the async factory path.");

    public Task<LibraryContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new LibraryContext(options);
        Contexts.Add(context);
        return Task.FromResult(context);
    }
}