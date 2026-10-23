using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class OverlappingReferenceContext(DbContextOptions<OverlappingReferenceContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OverlappingReferenceOwner>().HasKey(row => new { row.PartitionId, row.ZoneId, row.Id });
        modelBuilder.Entity<OverlappingReferenceOwner>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<OverlappingReferenceTarget>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<OverlappingReferenceActor>().HasKey(row => row.Id);
        modelBuilder.Entity<OverlappingReferenceActor>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<OverlappingReferenceActor>().HasAlternateKey(row => new { row.PartitionId, row.Code });
        modelBuilder.Entity<OverlappingReferenceSource>().HasKey(row => new { row.ZoneId, row.Id });
        var link = modelBuilder.Entity<OverlappingReferenceLink>();
        link.HasKey(row => new { row.Scope, row.Zone, row.OwnerId, row.TargetId });
        link.HasOne(row => row.Owner).WithMany(row => row.Links).HasForeignKey(row => new { row.Zone, row.OwnerId, row.Scope })
            .HasPrincipalKey(row => new { row.ZoneId, row.Id, row.PartitionId }).IsRequired();
        link.HasOne(row => row.Target).WithMany().HasForeignKey(row => new { row.Scope, row.TargetId }).IsRequired();
        link.HasOne(row => row.Actor).WithMany().HasForeignKey(row => new { row.Scope, row.ActorCode })
            .HasPrincipalKey(row => new { row.PartitionId, row.Code }).IsRequired();
        link.HasOne(row => row.Reviewer).WithMany().HasForeignKey(row => new { row.Scope, row.ReviewerCode })
            .HasPrincipalKey(row => new { row.PartitionId, row.Code }).IsRequired();
        link.HasOne(row => row.Source).WithMany().HasForeignKey(row => new { row.Zone, row.SourceId }).IsRequired();
        link.Property(row => row.Version).IsConcurrencyToken();
        link.ToTable(table => table.HasCheckConstraint("CK_OverlapNote", "\"Note\" <> 'invalid'"));
    }
}
