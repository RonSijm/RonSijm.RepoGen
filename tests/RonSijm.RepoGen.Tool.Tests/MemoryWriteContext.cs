using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class MemoryWriteContext(DbContextOptions<MemoryWriteContext> options) : DbContext(options)
{
    public DbSet<MemoryWriteNote> Notes => Set<MemoryWriteNote>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryWriteNote>(entity => { entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever(); entity.Property(row => row.Version).IsConcurrencyToken(); });
        modelBuilder.Entity<MemoryWriteAudit>(entity => { entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever(); });
    }
}
