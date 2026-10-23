using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicContext(DbContextOptions<MappedAtomicContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<MappedStateNote>();
        note.Property(row => row.Id).ValueGeneratedNever();
        note.HasIndex(row => row.Title).IsUnique();
        note.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasComputedColumnSql("upper(\"Title\")", stored: true);
        note.Property<int>("audit").HasComputedColumnSql("\"Counter\" + 10", stored: true);
        note.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
        note.Property<int?>("OptionalVersion").HasDefaultValue(1).IsConcurrencyToken();
        note.IndexerProperty<byte[]?>("Stamp").HasDefaultValue(new byte[] { 1, 2 }).IsConcurrencyToken();
        note.HasQueryFilter(row => EF.Property<int>(row, "Revision") < 10);
    }
}
