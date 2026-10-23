using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeFilters;

public sealed class NativeNotebookContext(DbContextOptions<NativeNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredNativeNote>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasQueryFilter(row => !row.Deleted);
        });
    }
}
