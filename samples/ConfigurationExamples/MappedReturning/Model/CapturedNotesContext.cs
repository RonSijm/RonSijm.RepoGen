using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedReturning;

public sealed class CapturedNotesContext(DbContextOptions<CapturedNotesContext> options) : DbContext(options)
{
    public DbSet<CapturedNote> Notes => Set<CapturedNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<CapturedNote>();
        note.Property(row => row.NormalizedTitle).HasField("normalizedTitle").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasComputedColumnSql("upper([Title])", stored: true);
        note.Property<int>("version").HasField("version")
            .HasComputedColumnSql("[ChangeCounter] + 1", stored: true).IsConcurrencyToken();
        note.IndexerProperty<string?>("SourceTag").HasDefaultValue("api");
        note.Property<bool>("Archived").HasDefaultValue(false);
        note.HasQueryFilter(row => !EF.Property<bool>(row, "Archived"));
    }
}
