using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScalarEquality;

public sealed class ComparisonNotesContext(DbContextOptions<ComparisonNotesContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ComparedNote>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Caption).Metadata.SetValueComparer(new ValueComparer<string?>(
                (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                value => value == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value), value => value));
            // CLR token comparison and SQL comparison are separate choices; this collation is SQLite-specific.
            entity.Property(row => row.Revision).IsConcurrencyToken().UseCollation("NOCASE").Metadata.SetValueComparer(new ValueComparer<string>(
                (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                value => StringComparer.OrdinalIgnoreCase.GetHashCode(value), value => value));
        });
    }
}
