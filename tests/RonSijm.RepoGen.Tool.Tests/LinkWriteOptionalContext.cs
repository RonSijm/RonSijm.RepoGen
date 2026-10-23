using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteOptionalContext(DbContextOptions<LinkWriteContext> options) : LinkWriteContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LinkWriteRow>().HasOne(row => row.Target).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.TargetRegion, row.TargetCode }).IsRequired(false);
    }
}
