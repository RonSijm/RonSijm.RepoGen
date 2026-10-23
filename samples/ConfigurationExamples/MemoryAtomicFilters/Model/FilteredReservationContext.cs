using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicFilters;

public sealed class FilteredReservationContext(DbContextOptions<FilteredReservationContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilteredReservation>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.Scope, row.Code }).IsUnique();
            entity.HasQueryFilter(row => !row.Deleted);
        });
    }
}
