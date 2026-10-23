using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredCollectionKeyContext(DbContextOptions<MemoryCollectionContext> options) : FilteredCollectionContext(options)
{
    public int Partition => throw new InvalidOperationException(GetType().Name + " must not be constructed/read by fixture execution.");
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MemoryCollectionChild>().HasQueryFilter(row => row.ParentId == Partition && row.Text != "deleted");
    }
}
