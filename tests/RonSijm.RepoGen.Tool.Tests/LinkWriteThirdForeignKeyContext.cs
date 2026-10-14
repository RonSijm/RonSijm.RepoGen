using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteThirdForeignKeyContext(DbContextOptions<LinkWriteContext> options) : LinkWriteContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LinkWriteRow>().HasOne<LinkWriteOwner>().WithMany().HasForeignKey("ReviewerTenantId", "ReviewerOwnerId");
    }
}
