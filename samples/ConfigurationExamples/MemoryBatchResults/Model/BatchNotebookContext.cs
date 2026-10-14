using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryBatchResults;

public sealed class BatchNotebookContext(DbContextOptions<BatchNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BatchNotebook>().HasKey(row => new { row.TenantId, row.Id });
        modelBuilder.Entity<BatchNotebook>().HasIndex(row => row.Code).IsUnique();
        modelBuilder.Entity<BatchNote>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<BatchNote>().HasOne(row => row.Notebook).WithMany(row => row.Notes).HasForeignKey(row => new { row.TenantId, row.NotebookId });
    }
}
