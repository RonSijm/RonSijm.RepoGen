using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCompoundCollectionContext(DbContextOptions<MemoryCompoundCollectionContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryCompoundParent>().HasKey(row => new { row.Id, row.Region });
        modelBuilder.Entity<MemoryCompoundChild>().HasOne<MemoryCompoundParent>().WithMany(row => row.Children).HasForeignKey(row => new { row.ParentId, row.Region });
        modelBuilder.Entity<MemoryCompoundChild>().HasIndex(row => new { row.ParentId, row.Region, row.Code }).IsUnique();
    }
}
