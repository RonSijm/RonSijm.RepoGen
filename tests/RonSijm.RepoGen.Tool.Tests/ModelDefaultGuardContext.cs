using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelDefaultGuardContext(DbContextOptions<ModelDefaultContext> options, string mode) : ModelDefaultContext(options)
{
    internal string Mode { get; } = mode;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); var property = modelBuilder.Entity<ModelDefaultRow>().Property(row => row.Credits);
        switch (Mode)
        {
            case "sql": property.HasDefaultValue((object?)null).HasDefaultValueSql("(5)"); break;
            case "computed": property.HasDefaultValue((object?)null).HasComputedColumnSql("5"); break;
            case "converter": property.HasConversion<long>(); break;
            case "comparer": property.Metadata.SetValueComparer(new ValueComparer<int>((left, right) => left == right, value => value, value => value)); break;
            case "never": property.ValueGeneratedNever(); break;
            case "key": modelBuilder.Entity<ModelDefaultRow>().Property(row => row.Id).HasDefaultValue(20).ValueGeneratedOnAdd(); break;
            default: throw new InvalidOperationException("Unknown guard: " + Mode);
        }
    }
}
