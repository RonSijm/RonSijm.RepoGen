using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredLinkCollectionContext(DbContextOptions<LinkWriteContext> options) : LinkWriteContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LinkWriteRow>().HasQueryFilter(row => row.Note != "deleted");
        modelBuilder.Entity<LinkWriteTarget>().HasQueryFilter(row => !row.Hidden);
    }
}
