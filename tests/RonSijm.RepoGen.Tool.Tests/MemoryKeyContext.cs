using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryKeyContext(DbContextOptions<MemoryKeyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<MemoryKeyRow>();
        row.HasKey(value => value.Code); row.Property(value => value.Code).ValueGeneratedNever();
        row.Property(value => value.Version).IsConcurrencyToken();
        modelBuilder.Entity<MemoryKeyCompoundRow>().HasKey(value => new { value.Code, value.Slot });
    }
}
