using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredNavigation;

public sealed class FilteredShelfContext(DbContextOptions<FilteredShelfContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredFilteredShelf>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasQueryFilter(row => !row.Archived);
        });
        modelBuilder.Entity<StoredFilteredEntry>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasQueryFilter(row => !row.Deleted);
            entity.HasOne(row => row.Shelf).WithMany(shelf => shelf.Entries).HasForeignKey(row => row.ShelfId);
        });
    }
}
