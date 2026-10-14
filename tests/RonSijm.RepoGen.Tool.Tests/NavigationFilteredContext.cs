using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationFilteredContext(DbContextOptions<NavigationReadContext> options) : NavigationReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<NavigationReadAuthor>().HasQueryFilter(row => row.Name != "Hidden");
    }
}
