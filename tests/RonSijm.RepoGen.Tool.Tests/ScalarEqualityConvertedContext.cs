using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScalarEqualityConvertedContext(DbContextOptions<ScalarEqualityContext> options) : ScalarEqualityContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ScalarEqualityRow>().Property(row => row.Label).HasConversion(new ValueConverter<string?, string?>(value => value, value => value));
    }
}
