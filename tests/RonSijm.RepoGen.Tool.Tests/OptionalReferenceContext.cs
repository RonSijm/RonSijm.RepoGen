using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class OptionalReferenceContext(DbContextOptions<OptionalReferenceContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OptionalReferenceOwner>().HasKey(row => row.Id);
        modelBuilder.Entity<OptionalReferenceOwner>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<OptionalReferenceOwner>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<OptionalReferenceTarget>().HasKey(row => row.Id);
        modelBuilder.Entity<OptionalReferenceTarget>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<OptionalReferenceReviewer>().HasKey(row => row.Id);
        modelBuilder.Entity<OptionalReferenceReviewer>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<OptionalReferenceReviewer>().HasAlternateKey(row => new { row.Region, row.Code });
        modelBuilder.Entity<OptionalReferenceSource>().HasKey(row => row.Id);
        modelBuilder.Entity<OptionalReferenceSource>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<OptionalReferenceRow>().HasKey(row => new { row.OwnerId, row.TargetId });
        modelBuilder.Entity<OptionalReferenceRow>().HasOne(row => row.Owner).WithMany(row => row.Links).HasForeignKey(row => row.OwnerId).IsRequired();
        modelBuilder.Entity<OptionalReferenceRow>().HasOne(row => row.Target).WithMany(row => row.Links).HasForeignKey(row => row.TargetId).IsRequired();
        modelBuilder.Entity<OptionalReferenceRow>().HasOne(row => row.Reviewer).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.ReviewerRegion, row.ReviewerCode }).HasPrincipalKey(row => new { row.Region, row.Code }).IsRequired(false);
        modelBuilder.Entity<OptionalReferenceRow>().HasOne(row => row.Source).WithMany(row => row.Links).HasForeignKey(row => row.SourceId).IsRequired(false);
        modelBuilder.Entity<OptionalReferenceRow>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<OptionalReferenceRow>().ToTable(table => table.HasCheckConstraint("CK_OptionalNote", "\"Note\" <> 'invalid'"));
    }
}
