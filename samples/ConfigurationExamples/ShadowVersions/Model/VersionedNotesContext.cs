using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.ShadowVersions;

public sealed class VersionedNotesContext(DbContextOptions<VersionedNotesContext> options) : DbContext(options)
{
    public DbSet<VersionedNote> Notes => Set<VersionedNote>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<VersionedNote>();
        // SQLite demonstration, not SQL Server rowversion emulation: every designed update increments ChangeCounter.
        note.Property<int>("Version").HasComputedColumnSql("\"ChangeCounter\" + 1", stored: true).IsConcurrencyToken();
        note.Property<bool>("Archived").HasDefaultValue(false);
        note.HasQueryFilter(row => !EF.Property<bool>(row, "Archived"));
    }
}
