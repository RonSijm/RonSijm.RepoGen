using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RonSijm.RepoGen.Tool.Tests;

public class ScalarEqualityContext(DbContextOptions<ScalarEqualityContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScalarEqualityRow>(entity =>
        {
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Label).Metadata.SetValueComparer(new ValueComparer<string?>(
                (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase), value => value == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value), value => value));
            entity.Property(row => row.Revision).IsConcurrencyToken().UseCollation("NOCASE").Metadata.SetValueComparer(new ValueComparer<string>(
                (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase), value => StringComparer.OrdinalIgnoreCase.GetHashCode(value), value => value));
        });
    }
}
