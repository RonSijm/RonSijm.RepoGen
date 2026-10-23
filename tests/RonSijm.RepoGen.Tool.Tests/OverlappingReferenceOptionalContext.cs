using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceOptionalContext(DbContextOptions<OverlappingReferenceContext> options) : OverlappingReferenceContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OverlappingReferenceLink>().Property(row => row.ReviewerCode).IsRequired(false);
        modelBuilder.Entity<OverlappingReferenceLink>().HasOne(row => row.Reviewer).WithMany().HasForeignKey(row => new { row.Scope, row.ReviewerCode })
            .HasPrincipalKey(row => new { row.PartitionId, row.Code }).IsRequired(false);
    }
}
