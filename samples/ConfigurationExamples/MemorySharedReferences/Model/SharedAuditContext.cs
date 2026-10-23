using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedReferences;

public sealed class SharedAuditContext(DbContextOptions<SharedAuditContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditFolder>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<AuditFolder>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<AuditResource>().HasKey(row => row.Id);
        modelBuilder.Entity<AuditResource>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<AuditActor>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<AuditLink>().HasKey(row => new { row.PartitionId, row.FolderId, row.ResourceId });
        modelBuilder.Entity<AuditLink>().HasOne(row => row.Folder).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.PartitionId, row.FolderId }).IsRequired();
        modelBuilder.Entity<AuditLink>().HasOne(row => row.Resource).WithMany().HasForeignKey(row => row.ResourceId).IsRequired();
        modelBuilder.Entity<AuditLink>().HasOne(row => row.Actor).WithMany().HasForeignKey(row => new { row.PartitionId, row.ActorId }).IsRequired();
        modelBuilder.Entity<AuditLink>().Property(row => row.Version).IsConcurrencyToken();
    }
}
