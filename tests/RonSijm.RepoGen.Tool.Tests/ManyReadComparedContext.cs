using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ManyReadComparedContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadLink>().Property(row => row.OwnerCode).Metadata.SetValueComparer(new ValueComparer<string>(
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            value => StringComparer.OrdinalIgnoreCase.GetHashCode(value), value => value));
    }
}
