using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.ProxyShelves;

public sealed class ProxyShelfContext(DbContextOptions<ProxyShelfContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShelfRecord>().HasAlternateKey(row => new { row.Code, row.Region });
        modelBuilder.Entity<ShelfRecord>().HasMany(row => row.Items).WithOne(row => row.Shelf)
            .HasForeignKey(row => new { row.ShelfCode, row.ShelfRegion }).HasPrincipalKey(row => new { row.Code, row.Region });
        modelBuilder.Entity<ShelfItemRecord>().HasOne(row => row.Category).WithMany().HasForeignKey(row => row.CategoryId).IsRequired(false);
    }
}
