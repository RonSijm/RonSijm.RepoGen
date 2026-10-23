using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredCollections;

public sealed class FilteredChecklistContext(DbContextOptions<FilteredChecklistContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilteredChecklist>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<FilteredChecklist>().Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<FilteredChecklist>().HasQueryFilter(row => !row.Archived);
        modelBuilder.Entity<FilteredChecklistItem>().HasOne(row => row.Checklist).WithMany(row => row.Items).HasForeignKey(row => row.ChecklistId);
        modelBuilder.Entity<FilteredChecklistItem>().HasIndex(row => new { row.ChecklistId, row.Code }).IsUnique();
        modelBuilder.Entity<FilteredChecklistItem>().Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<FilteredChecklistItem>().HasQueryFilter(row => !row.Archived);
    }
}
