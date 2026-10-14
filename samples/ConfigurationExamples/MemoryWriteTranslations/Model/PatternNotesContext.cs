using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryWriteTranslations;

public sealed class PatternNotesContext(DbContextOptions<PatternNotesContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredPatternNote>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Version).IsConcurrencyToken();
        });
        modelBuilder.HasDbFunction(typeof(PatternNoteFunctions).GetMethod(nameof(PatternNoteFunctions.Length))!).HasName("length");
    }
}
