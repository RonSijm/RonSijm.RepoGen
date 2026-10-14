using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class RelationalFixtureContext(DbContextOptions<RelationalFixtureContext> options) : DbContext(options)
{
    public Func<ValueTask>? BeforeDisposal { get; set; }
    public int Disposals { get; private set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<RelationalFixtureRow>();

    public override async ValueTask DisposeAsync()
    {
        Disposals++;
        await base.DisposeAsync();
        if (BeforeDisposal is not null) await BeforeDisposal();
    }
}
