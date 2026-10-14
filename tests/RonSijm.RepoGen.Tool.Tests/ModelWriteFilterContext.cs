using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelWriteFilterContext(DbContextOptions<ModelWriteFilterContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelWriteFilterNote>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasQueryFilter(row => !row.Deleted);
        });
        modelBuilder.Entity<MemoryWriteAudit>().Property(row => row.Id).ValueGeneratedNever();
    }
}
