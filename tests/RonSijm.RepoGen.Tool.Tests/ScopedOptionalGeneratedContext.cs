using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScopedOptionalGeneratedContext(DbContextOptions<ScopedOptionalContext> options) : ScopedOptionalContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ScopedOptionalRow>().Property(row => row.OriginId).ValueGeneratedOnAdd();
    }
}
