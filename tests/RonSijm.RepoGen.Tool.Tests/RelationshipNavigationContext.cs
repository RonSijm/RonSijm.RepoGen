using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RonSijm.RepoGen.Tool.Tests;

public class RelationshipNavigationContext(DbContextOptions<NavigationReadContext> options) : NavigationReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<NavigationReadLibrary>().Property(row => row.Code).UseCollation("NOCASE");
        modelBuilder.Entity<NavigationReadBook>().Property(row => row.LibraryCode).UseCollation("NOCASE");
        modelBuilder.Entity<NavigationReadLibrary>().Property(row => row.Code).Metadata.SetValueComparer(Comparer());
        modelBuilder.Entity<NavigationReadBook>().Property(row => row.LibraryCode).Metadata.SetValueComparer(Comparer());
    }

    internal static ValueComparer<string> Comparer() => new(
        (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
        value => StringComparer.OrdinalIgnoreCase.GetHashCode(value), value => value);
}
