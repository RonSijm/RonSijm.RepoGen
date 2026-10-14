using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelWriteFilterGraphContext(DbContextOptions<MemoryCollectionContext> options) : MemoryCollectionContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MemoryCollectionParent>().HasQueryFilter(row => row.OwnerId > 0);
    }
}
