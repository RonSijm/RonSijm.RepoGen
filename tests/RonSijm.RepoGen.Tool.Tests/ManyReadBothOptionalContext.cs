using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyReadBothOptionalContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadLink>().HasOne<ManyReadTag>().WithMany().HasForeignKey(row => row.TagId).IsRequired(false);
        modelBuilder.Entity<ManyReadLink>().HasOne<ManyReadOwner>().WithMany().HasForeignKey(row => new { row.TenantId, row.OwnerCode })
            .HasPrincipalKey(row => new { row.TenantId, row.Code }).IsRequired(false);
    }
}
