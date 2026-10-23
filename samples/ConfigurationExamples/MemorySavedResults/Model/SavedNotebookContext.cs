using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedResults;

public sealed class SavedNotebookContext(DbContextOptions<SavedNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SavedNotebook>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<SavedNote>().HasOne(row => row.Notebook).WithMany(row => row.Notes).HasForeignKey(row => row.NotebookId);
        modelBuilder.Entity<SavedNote>().HasIndex(row => new { row.NotebookId, row.Code }).IsUnique();
    }
}
