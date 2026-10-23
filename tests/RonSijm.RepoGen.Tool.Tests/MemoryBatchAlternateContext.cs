using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryBatchAlternateContext(DbContextOptions<MemoryWriteContext> options) : MemoryWriteContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MemoryWriteNote>().HasIndex(row => row.Title).IsUnique();
    }
}
