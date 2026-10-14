using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AutoIncludeReadContext(DbContextOptions<NavigationReadContext> options) : NavigationReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<NavigationReadLibrary>().Navigation(row => row.Books).AutoInclude();
        modelBuilder.Entity<NavigationReadBook>().Navigation(row => row.Author).AutoInclude();
    }
}
