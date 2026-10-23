using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionFilteredContext(DbContextOptions<MappedCollectionContext> options) : MappedCollectionContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MappedCollectionParent>().HasQueryFilter(row => EF.Property<int>(row, "Revision") > 0);
        modelBuilder.Entity<MappedCollectionChild>().HasQueryFilter(row => EF.Property<int>(row, "audit") > 0);
    }
}
