using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryGeneratedValues;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryGeneratedValues;

public sealed class GeneratedNotebookContext(DbContextOptions<GeneratedNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<GeneratedNote>();
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Origin).HasDefaultValue("designer");
        entity.Property(row => row.TextLength).HasComputedColumnSql("length(\"Text\")", stored: true).IsConcurrencyToken();
    }
}
