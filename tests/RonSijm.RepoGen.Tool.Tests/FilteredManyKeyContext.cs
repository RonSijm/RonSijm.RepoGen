using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredManyKeyContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    public int TenantId => throw new InvalidOperationException(GetType().Name + " must never be read by generation or fixtures.");
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadOwner>().HasQueryFilter(row => row.TenantId == TenantId);
        modelBuilder.Entity<ManyReadTag>().HasQueryFilter(row => row.Name != "Archived tag");
        modelBuilder.Entity<ManyReadLink>().HasQueryFilter(row => row.TenantId == TenantId && !row.Hidden);
    }
}
