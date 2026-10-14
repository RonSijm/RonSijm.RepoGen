using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelFilters;

public sealed class FilteredNotebookContext(DbContextOptions<FilteredNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredFilteredNote>(entity =>
        {
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasQueryFilter(row => !row.Deleted);
        });
        modelBuilder.Entity<StoredFilteredGroup>(entity =>
        {
            entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasQueryFilter(row => !row.Archived);
        });
    }
}
