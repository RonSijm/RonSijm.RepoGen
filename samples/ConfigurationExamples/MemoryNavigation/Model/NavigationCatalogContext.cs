using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNavigation;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNavigation;

public sealed class NavigationCatalogContext(DbContextOptions<NavigationCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogGroup>().HasMany(group => group.Entries).WithOne(entry => entry.Group)
            .HasForeignKey(entry => new { entry.GroupCode, entry.GroupRegion })
            .HasPrincipalKey(group => new { group.Code, group.Region });
        modelBuilder.Entity<CatalogEntry>().HasOne(entry => entry.Category).WithMany().HasForeignKey(entry => entry.CategoryId);
    }
}
