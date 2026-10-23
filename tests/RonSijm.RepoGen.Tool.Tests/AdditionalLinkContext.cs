using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class AdditionalLinkContext(DbContextOptions<AdditionalLinkContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdditionalLinkOwner>().HasKey(row => new { row.Scope, row.Id });
        modelBuilder.Entity<AdditionalLinkOwner>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<AdditionalLinkTarget>().HasKey(row => new { row.Scope, row.Id });
        modelBuilder.Entity<AdditionalLinkActor>().HasKey(row => row.Id);
        modelBuilder.Entity<AdditionalLinkActor>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<AdditionalLinkActor>().HasAlternateKey(row => new { row.Region, row.Code });
        modelBuilder.Entity<AdditionalLinkSource>().HasKey(row => row.Id);
        modelBuilder.Entity<AdditionalLinkSource>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<AdditionalLinkRow>().HasKey(row => new { row.Scope, row.OwnerId, row.TargetId });
        modelBuilder.Entity<AdditionalLinkRow>().HasOne(row => row.Owner).WithMany(row => row.Links).HasForeignKey(row => new { row.Scope, row.OwnerId }).IsRequired();
        modelBuilder.Entity<AdditionalLinkRow>().HasOne(row => row.Target).WithMany(row => row.Links).HasForeignKey(row => new { row.Scope, row.TargetId }).IsRequired();
        modelBuilder.Entity<AdditionalLinkRow>().HasOne(row => row.Actor).WithMany(row => row.Links).HasForeignKey(row => new { row.ActorRegion, row.ActorCode })
            .HasPrincipalKey(row => new { row.Region, row.Code }).IsRequired();
        modelBuilder.Entity<AdditionalLinkRow>().HasOne(row => row.Source).WithMany(row => row.Links).HasForeignKey(row => row.SourceId).IsRequired();
        modelBuilder.Entity<AdditionalLinkRow>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<AdditionalLinkRow>().ToTable(table => table.HasCheckConstraint("CK_AdditionalNote", "\"Note\" <> 'invalid'"));
    }
}
