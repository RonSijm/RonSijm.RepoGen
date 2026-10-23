using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedFilters;

public sealed class SavedShelfContext(DbContextOptions<SavedShelfContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredSavedShelf>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasQueryFilter(row => !row.Archived);
        });
        modelBuilder.Entity<StoredSavedEntry>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasQueryFilter(row => !row.Deleted);
            entity.HasOne(row => row.Shelf).WithMany(shelf => shelf.Entries).HasForeignKey(row => row.ShelfId);
        });
    }
}
