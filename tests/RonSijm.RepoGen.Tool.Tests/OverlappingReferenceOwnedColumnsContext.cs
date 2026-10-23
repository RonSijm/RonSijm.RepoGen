using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OverlappingReferenceOwnedColumnsContext(DbContextOptions<OverlappingReferenceContext> options) : OverlappingReferenceContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OverlappingReferenceLink>().HasOne(row => row.Source).WithMany().HasForeignKey(row => new { row.Zone, row.OwnerId }).IsRequired();
    }
}
