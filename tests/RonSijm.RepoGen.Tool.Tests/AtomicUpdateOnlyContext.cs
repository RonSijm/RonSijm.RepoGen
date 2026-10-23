using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicUpdateOnlyContext(DbContextOptions<AtomicUpdateOnlyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ImmediateRow>();
        entity.HasIndex(row => new { row.OwnerId, row.Code }).IsUnique();
        entity.Property(row => row.Previous).ValueGeneratedOnUpdate();
        entity.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        entity.Property(row => row.Stamp).IsConcurrencyToken().ValueGeneratedNever();
    }
}
