using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryGeneratedWriteContext(DbContextOptions<MemoryWriteContext> options) : MemoryWriteContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MemoryWriteNote>().Property(row => row.Id).ValueGeneratedOnAdd();
    }
}
