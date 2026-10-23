using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyConvertedContext(DbContextOptions<MappedKeyContext> options) : MappedKeyContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MappedKeyOwner>().Property(row => row.Code).HasConversion<string>(value => value, value => value);
    }
}
