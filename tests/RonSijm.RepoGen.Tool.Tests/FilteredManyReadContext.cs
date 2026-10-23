using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class FilteredManyReadContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadOwner>().HasQueryFilter(row => row.Name != "Archived owner");
        modelBuilder.Entity<ManyReadTag>().HasQueryFilter(row => row.Name != "Archived tag");
        modelBuilder.Entity<ManyReadLink>().HasQueryFilter(row => !row.Hidden);
    }
}
