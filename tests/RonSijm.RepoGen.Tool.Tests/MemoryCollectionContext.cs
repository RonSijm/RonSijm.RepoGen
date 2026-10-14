using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class MemoryCollectionContext(DbContextOptions<MemoryCollectionContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryCollectionParent>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<MemoryCollectionParent>().Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<MemoryCollectionChild>().HasOne(row => row.Parent).WithMany(row => row.Children).HasForeignKey(row => row.ParentId);
        modelBuilder.Entity<MemoryCollectionChild>().HasIndex(row => new { row.ParentId, row.Key }).IsUnique();
        modelBuilder.Entity<MemoryCollectionChild>().Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
    }
}
