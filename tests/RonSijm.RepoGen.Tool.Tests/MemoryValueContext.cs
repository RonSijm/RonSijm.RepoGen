using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryValueContext(DbContextOptions<MemoryValueContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MemoryValueRow>();
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Label).HasDefaultValue("fixture");
        entity.Property(row => row.TitleLength).HasComputedColumnSql("length(\"Title\")", stored: true).IsConcurrencyToken();
    }
}
