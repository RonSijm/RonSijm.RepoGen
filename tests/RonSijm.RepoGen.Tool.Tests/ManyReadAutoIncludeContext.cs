using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyReadAutoIncludeContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadOwner>().Navigation(row => row.Tags).AutoInclude();
    }
}
