using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScopedOptional;

public sealed class ScopedReviewContext(DbContextOptions<ScopedReviewContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewFolder>().HasKey(row => new { row.PartitionId, row.Id });
        modelBuilder.Entity<ReviewFolder>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<ReviewResource>().HasKey(row => row.Id);
        modelBuilder.Entity<ReviewResource>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<ReviewPerson>().HasKey(row => row.Id);
        modelBuilder.Entity<ReviewPerson>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<ReviewLink>().HasKey(row => new { row.PartitionId, row.FolderId, row.ResourceId });
        modelBuilder.Entity<ReviewLink>().HasOne(row => row.Folder).WithMany(row => row.Links).HasForeignKey(row => new { row.PartitionId, row.FolderId }).IsRequired();
        modelBuilder.Entity<ReviewLink>().HasOne(row => row.Resource).WithMany().HasForeignKey(row => row.ResourceId).IsRequired();
        modelBuilder.Entity<ReviewLink>().HasOne(row => row.Reviewer).WithMany()
            .HasForeignKey(row => new { row.ReviewerRegion, row.PartitionId, row.ReviewerCode })
            .HasPrincipalKey(row => new { row.Region, row.PartitionId, row.Code }).IsRequired(false);
        modelBuilder.Entity<ReviewLink>().Property(row => row.Version).IsConcurrencyToken();
    }
}
