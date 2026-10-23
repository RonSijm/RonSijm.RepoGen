using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionFilteredContext(DbContextOptions<MemoryCollectionContext> options) : MemoryCollectionContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MemoryCollectionChild>().HasQueryFilter(row => !row.Hidden);
    }
}
