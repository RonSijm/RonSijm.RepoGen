using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AdditionalLinkOptionalContext(DbContextOptions<AdditionalLinkContext> options) : AdditionalLinkContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AdditionalLinkRow>().HasOne(row => row.Actor).WithMany(row => row.Links)
            .HasForeignKey(row => new { row.ActorRegion, row.ActorCode }).HasPrincipalKey(row => new { row.Region, row.Code }).IsRequired(false);
    }
}
