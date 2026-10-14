using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipManyComparedContext(DbContextOptions<ManyReadContext> options) : RelationshipManyContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadOwner>().Property(row => row.Code).Metadata.SetValueComparer(RelationshipNavigationContext.Comparer());
        modelBuilder.Entity<ManyReadLink>().Property(row => row.OwnerCode).Metadata.SetValueComparer(RelationshipNavigationContext.Comparer());
    }
}
