using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipNavigationConvertedContext(DbContextOptions<NavigationReadContext> options) : RelationshipNavigationContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<NavigationReadLibrary>().Property(row => row.Code).HasConversion(value => value.ToUpperInvariant(), value => value);
        modelBuilder.Entity<NavigationReadBook>().Property(row => row.LibraryCode).HasConversion(value => value == null ? null : value.ToUpperInvariant(), value => value);
    }
}
