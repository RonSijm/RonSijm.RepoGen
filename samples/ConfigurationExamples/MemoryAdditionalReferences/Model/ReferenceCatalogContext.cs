using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;

public sealed class ReferenceCatalogContext(DbContextOptions<ReferenceCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredCollection>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredCollection>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<StoredCatalogItem>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredContributor>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredContributor>().HasAlternateKey(row => new { row.Region, row.Code });
        modelBuilder.Entity<StoredImportSource>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredMembership>().HasKey(row => new { row.CollectionId, row.ItemId });
        modelBuilder.Entity<StoredMembership>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<StoredMembership>().HasOne(row => row.Collection).WithMany(row => row.Memberships).HasForeignKey(row => row.CollectionId).IsRequired();
        modelBuilder.Entity<StoredMembership>().HasOne(row => row.Item).WithMany(row => row.Memberships).HasForeignKey(row => row.ItemId).IsRequired();
        modelBuilder.Entity<StoredMembership>().HasOne(row => row.Contributor).WithMany(row => row.Memberships)
            .HasForeignKey(row => new { row.ContributorRegion, row.ContributorCode }).HasPrincipalKey(row => new { row.Region, row.Code }).IsRequired();
        modelBuilder.Entity<StoredMembership>().HasOne(row => row.Source).WithMany(row => row.Memberships).HasForeignKey(row => row.SourceId).IsRequired();
    }
}
