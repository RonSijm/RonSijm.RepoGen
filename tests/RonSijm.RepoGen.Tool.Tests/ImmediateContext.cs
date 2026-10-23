using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ImmediateContext(DbContextOptions<ImmediateContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ImmediateRow>();
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        entity.Property(row => row.Stamp).IsConcurrencyToken().ValueGeneratedNever();
    }
}
