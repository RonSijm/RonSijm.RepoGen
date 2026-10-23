using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class ScopedOptionalContext(DbContextOptions<ScopedOptionalContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScopedOptionalOwner>().HasKey(row => new { row.PartitionId, row.ZoneId, row.Id });
        modelBuilder.Entity<ScopedOptionalOwner>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<ScopedOptionalTarget>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<ScopedOptionalReviewer>().HasKey(row => row.Id);
        modelBuilder.Entity<ScopedOptionalReviewer>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<ScopedOptionalReviewer>().HasAlternateKey(row => new { row.Region, row.PartitionId, row.Code });
        modelBuilder.Entity<ScopedOptionalOrigin>().HasKey(row => new { row.ZoneId, row.Id });
        var link = modelBuilder.Entity<ScopedOptionalRow>();
        link.HasKey(row => new { row.Scope, row.Zone, row.OwnerId, row.TargetId });
        link.HasOne(row => row.Owner).WithMany(row => row.Links).HasForeignKey(row => new { row.Zone, row.OwnerId, row.Scope })
            .HasPrincipalKey(row => new { row.ZoneId, row.Id, row.PartitionId }).IsRequired();
        link.HasOne(row => row.Target).WithMany().HasForeignKey(row => new { row.Scope, row.TargetId }).IsRequired();
        link.HasOne(row => row.Actor).WithMany().HasForeignKey(row => new { row.ActorRegion, row.Scope, row.ActorCode })
            .HasPrincipalKey(row => new { row.Region, row.PartitionId, row.Code }).IsRequired();
        link.HasOne(row => row.Reviewer).WithMany().HasForeignKey(row => new { row.ReviewerRegion, row.Scope, row.ReviewerCode })
            .HasPrincipalKey(row => new { row.Region, row.PartitionId, row.Code }).IsRequired(false);
        link.HasOne(row => row.Origin).WithMany().HasForeignKey(row => new { row.OriginId, row.Zone })
            .HasPrincipalKey(row => new { row.Id, row.ZoneId }).IsRequired(false);
        link.Property(row => row.Version).IsConcurrencyToken();
        link.ToTable(table => table.HasCheckConstraint("CK_ScopedOptionalNote", "\"Note\" <> 'invalid'"));
    }
}
