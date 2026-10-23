using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class TranslatedWriteContext(DbContextOptions<TranslatedWriteContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryWriteNote>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Version).IsConcurrencyToken();
        });
        modelBuilder.HasDbFunction(typeof(TranslationCalls).GetMethod(nameof(TranslationCalls.Measure))!).HasName("length");
    }
}
