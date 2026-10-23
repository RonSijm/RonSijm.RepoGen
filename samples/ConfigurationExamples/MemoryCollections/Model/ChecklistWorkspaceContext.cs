using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCollections;

public sealed class ChecklistWorkspaceContext(DbContextOptions<ChecklistWorkspaceContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredChecklist>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredChecklist>().Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<StoredChecklistItem>().HasOne(row => row.Checklist).WithMany(row => row.Items).HasForeignKey(row => row.ChecklistId);
        modelBuilder.Entity<StoredChecklistItem>().HasIndex(row => new { row.ChecklistId, row.Code }).IsUnique();
        modelBuilder.Entity<StoredChecklistItem>().Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
    }
}
