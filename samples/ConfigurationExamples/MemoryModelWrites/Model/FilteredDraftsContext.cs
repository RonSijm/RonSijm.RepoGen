using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelWrites;

public sealed class FilteredDraftsContext(DbContextOptions<FilteredDraftsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredFilteredDraft>(entity =>
        {
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasQueryFilter(row => !row.Archived);
        });
        modelBuilder.Entity<StoredFilteredAudit>(entity =>
        {
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
        });
    }
}
