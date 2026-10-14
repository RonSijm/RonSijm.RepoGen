using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNullTargets;

public sealed class NullTargetNotebookContext(DbContextOptions<NullTargetNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredNotebook>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredNotebook>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredNotebookLabel>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredNotebookLabel>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredNotebook>().HasMany(row => row.Labels).WithMany(row => row.Notebooks).UsingEntity<StoredNotebookLink>(
            right => right.HasOne<StoredNotebookLabel>().WithMany().HasForeignKey(row => row.LabelId).IsRequired(false),
            left => left.HasOne<StoredNotebook>().WithMany().HasForeignKey(row => row.NotebookId).IsRequired(),
            link => { link.HasKey(row => row.Id); link.Property(row => row.Id).ValueGeneratedNever(); });
    }
}
