using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicFixtureContext(DbContextOptions<AtomicFixtureContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ImmediateRow>();
        entity.Property(row => row.Id).ValueGeneratedOnAdd();
        entity.HasIndex(row => new { row.OwnerId, row.Code }).IsUnique();
        entity.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        entity.Property(row => row.Stamp).IsConcurrencyToken().ValueGeneratedNever();
    }
}
