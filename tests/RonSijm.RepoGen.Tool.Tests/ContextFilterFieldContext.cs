using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterFieldContext(DbContextOptions<ContextFilterFieldContext> options) : DbContext(options)
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Intentional public-field input for the binding regression test.")]
    public readonly string Tenant = "not the fixture value";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelFilterRow>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<ModelFilterRow>().Ignore(row => row.Unmapped);
        modelBuilder.Entity<ModelFilterRow>().HasQueryFilter(row => row.Title == Tenant);
    }
}
