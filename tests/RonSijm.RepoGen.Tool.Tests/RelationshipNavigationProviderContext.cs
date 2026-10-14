using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipNavigationProviderContext(DbContextOptions<NavigationReadContext> options) : RelationshipNavigationContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<NavigationReadLibrary>().Property(row => row.Code).Metadata.SetProviderValueComparer(Comparer());
    }
}
