using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedCollections;

public sealed class VersionedChecklistContext(DbContextOptions<VersionedChecklistContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var checklist = modelBuilder.Entity<VersionedChecklist>();
        checklist.Property(row => row.Id).ValueGeneratedNever();
        checklist.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
        checklist.HasMany(row => row.Items).WithOne().HasForeignKey(row => row.ChecklistId);
        var item = modelBuilder.Entity<VersionedChecklistItem>();
        item.Property(row => row.Id).ValueGeneratedNever();
        item.HasIndex(row => new { row.ChecklistId, row.Code }).IsUnique();
        item.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
    }
}
