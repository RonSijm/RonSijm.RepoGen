using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class TranslatedReadContext(DbContextOptions<TranslatedReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortableRow>();
        modelBuilder.HasDbFunction(typeof(TranslationCalls).GetMethod(nameof(TranslationCalls.Measure))!).HasName("length");
    }
}
