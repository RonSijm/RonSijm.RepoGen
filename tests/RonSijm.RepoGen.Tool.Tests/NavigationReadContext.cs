using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class NavigationReadContext(DbContextOptions<NavigationReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder;
        builder.Entity<NavigationReadLibrary>().HasAlternateKey(row => new { row.Code, row.Region });
        builder.Entity<NavigationReadLibrary>().HasMany(row => row.Books).WithOne(row => row.Library)
            .HasForeignKey(row => new { row.LibraryCode, row.LibraryRegion }).HasPrincipalKey(row => new { row.Code, row.Region });
        builder.Entity<NavigationReadLibrary>().HasOne(row => row.Profile).WithOne().HasForeignKey<NavigationReadProfile>(row => row.LibraryId);
        builder.Entity<NavigationReadBook>().HasOne(row => row.Author).WithMany().HasForeignKey(row => row.AuthorId).IsRequired();
        builder.Entity<NavigationReadBook>().HasOne(row => row.Parent).WithMany().HasForeignKey(row => row.ParentId);
    }
}
