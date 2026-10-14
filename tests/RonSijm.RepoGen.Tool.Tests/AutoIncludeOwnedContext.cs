using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AutoIncludeOwnedContext(DbContextOptions<AutoIncludeOwnedContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NavigationReadLibrary>().Ignore(row => row.Books);
        modelBuilder.Entity<NavigationReadLibrary>().OwnsOne(row => row.Profile);
    }
}
