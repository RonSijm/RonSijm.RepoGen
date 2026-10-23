using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedLinks;

public sealed class ScopedResourcesContext(DbContextOptions<ScopedResourcesContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredFolder>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<StoredFolder>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<StoredResource>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<StoredFolderResource>().HasKey(row => new { row.PartitionId, row.FolderId, row.ResourceId });
        modelBuilder.Entity<StoredFolderResource>().HasOne(row => row.Folder).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.PartitionId, row.FolderId }).IsRequired();
        modelBuilder.Entity<StoredFolderResource>().HasOne(row => row.Resource).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.PartitionId, row.ResourceId }).IsRequired();
        modelBuilder.Entity<StoredFolderResource>().Property(row => row.Version).IsConcurrencyToken();
    }
}
