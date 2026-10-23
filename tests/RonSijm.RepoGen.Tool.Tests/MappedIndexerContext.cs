using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedIndexerContext(DbContextOptions<MappedIndexerContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var text = modelBuilder.Entity<MappedTextIndexerRow>();
        text.ToTable("typed_text");
        text.Property(row => row.Id).ValueGeneratedNever();
        text.IndexerProperty<string>("Text").IsRequired();
        var binary = modelBuilder.Entity<MappedBinaryIndexerRow>();
        binary.ToTable("typed_binary");
        binary.Property(row => row.Id).ValueGeneratedNever();
        binary.IndexerProperty<byte[]>("Data").IsRequired();
    }
}
