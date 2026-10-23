using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkOverlapContext(DbContextOptions<AdditionalLinkContext> options) : AdditionalLinkContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AdditionalLinkRow>().HasOne(row => row.Source).WithMany(row => row.Links).HasForeignKey(row => row.TargetId).IsRequired();
    }
}
