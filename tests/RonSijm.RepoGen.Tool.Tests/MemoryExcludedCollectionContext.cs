using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryExcludedCollectionContext(DbContextOptions<MemoryExcludedCollectionContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NestedChild>().HasOne<MemoryExcludedCollectionParent>().WithMany(row => row.NestedChildren).HasForeignKey(row => row.ParentId);
        modelBuilder.Entity<NestedChild>().HasIndex(row => new { row.ParentId, row.Key }).IsUnique();
        modelBuilder.Entity<MemoryGenericCollectionChild<int>>().HasOne<MemoryExcludedCollectionParent>().WithMany(row => row.GenericChildren).HasForeignKey(row => row.ParentId);
        modelBuilder.Entity<MemoryGenericCollectionChild<int>>().HasIndex(row => new { row.ParentId, row.Key }).IsUnique();
    }

    // This regression intentionally supplies a nested EF child that repository eligibility excludes.
    public sealed class NestedChild
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Key { get; set; } = "";
    }
}
