using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SharedLinkContext(DbContextOptions<SharedLinkContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SharedLinkOwner>().HasKey(row => new { row.SpaceId, row.Id });
        modelBuilder.Entity<SharedLinkOwner>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<SharedLinkTarget>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<SharedLinkRow>().HasKey(row => new { row.Scope, row.OwnerId, row.TargetId });
        modelBuilder.Entity<SharedLinkRow>().HasOne(row => row.Owner).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.Scope, row.OwnerId }).HasPrincipalKey(row => new { row.SpaceId, row.Id }).IsRequired();
        modelBuilder.Entity<SharedLinkRow>().HasOne(row => row.Target).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.Scope, row.TargetId }).HasPrincipalKey(row => new { row.PartitionId, row.Id }).IsRequired();
        modelBuilder.Entity<SharedLinkRow>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<SharedLinkRow>().ToTable(table => table.HasCheckConstraint("CK_SharedLinkNote", "\"Note\" <> 'invalid'"));
    }
}
