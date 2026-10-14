using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedBatchContext(DbContextOptions<SavedBatchContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SavedBatchParent>().HasKey(row => new { row.TenantId, row.Id });
        modelBuilder.Entity<SavedBatchParent>().HasIndex(row => row.Code).IsUnique();
        modelBuilder.Entity<SavedBatchParent>().Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<SavedBatchChild>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<SavedBatchChild>().HasOne(row => row.Parent).WithMany(row => row.Children).HasForeignKey(row => new { row.TenantId, row.ParentId });
    }
}
