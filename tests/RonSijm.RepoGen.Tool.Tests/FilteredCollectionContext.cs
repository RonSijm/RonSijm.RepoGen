using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class FilteredCollectionContext(DbContextOptions<MemoryCollectionContext> options) : MemoryCollectionContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MemoryCollectionParent>().HasQueryFilter(row => row.OwnerId > 0);
        modelBuilder.Entity<MemoryCollectionChild>().HasQueryFilter(row => row.Text != "deleted");
    }
}
