using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyReadOptionalContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadLink>().HasOne<ManyReadTag>().WithMany().HasForeignKey(row => row.TagId).IsRequired(false);
    }
}
