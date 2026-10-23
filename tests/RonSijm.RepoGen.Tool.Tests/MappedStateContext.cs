using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedStateContext(DbContextOptions<MappedStateContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<MappedStateNote>();
        note.Property(row => row.Id).ValueGeneratedNever();
        note.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field).HasDefaultValue("caption");
        note.Property<int>("audit").HasDefaultValue(10);
        note.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
        note.Property<int?>("OptionalVersion").HasDefaultValue(1).IsConcurrencyToken();
        note.IndexerProperty<byte[]?>("Stamp").HasDefaultValue(new byte[] { 1, 2 }).IsConcurrencyToken();
    }
}
