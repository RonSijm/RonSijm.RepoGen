using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MultipleSharedLinkContext(DbContextOptions<MultipleSharedLinkContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MultipleSharedLinkOwner>().HasKey(row => new { row.Zone, row.Id, row.SpaceId });
        modelBuilder.Entity<MultipleSharedLinkTarget>().HasKey(row => row.Id);
        modelBuilder.Entity<MultipleSharedLinkTarget>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<MultipleSharedLinkTarget>().HasAlternateKey(row => new { row.Region, row.Code, row.Segment });
        modelBuilder.Entity<MultipleSharedLinkRow>().HasKey(row => new { row.Scope1, row.Scope2, row.OwnerId, row.TargetCode });
        modelBuilder.Entity<MultipleSharedLinkRow>().HasOne(row => row.Owner).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.Scope2, row.OwnerId, row.Scope1 }).IsRequired();
        modelBuilder.Entity<MultipleSharedLinkRow>().HasOne(row => row.Target).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.Scope1, row.TargetCode, row.Scope2 }).HasPrincipalKey(row => new { row.Region, row.Code, row.Segment }).IsRequired();
    }
}
