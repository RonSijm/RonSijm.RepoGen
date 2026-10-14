using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredNavigationManyContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadTag>().HasQueryFilter(row => !row.Hidden);
    }
}
