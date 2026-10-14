using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredNavigationPrincipalContext(DbContextOptions<NavigationReadContext> options) : NavigationReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<NavigationReadLibrary>().Navigation(row => row.Profile).IsRequired();
        modelBuilder.Entity<NavigationReadProfile>().HasQueryFilter(row => row.Caption != "filtered");
    }
}
