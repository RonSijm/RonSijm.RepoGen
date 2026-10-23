using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryTranslations;

public sealed class PatternCatalogContext(DbContextOptions<PatternCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredPatternEntry>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredPatternEntry>().Property(row => row.Id).ValueGeneratedNever();
        // The sample uses SQLite's native length function; other providers need their own mapping.
        modelBuilder.HasDbFunction(typeof(PatternFunctions).GetMethod(nameof(PatternFunctions.Length))!).HasName("length");
    }
}
