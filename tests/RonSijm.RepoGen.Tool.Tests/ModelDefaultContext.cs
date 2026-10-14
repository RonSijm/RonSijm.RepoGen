using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class ModelDefaultContext(DbContextOptions<ModelDefaultContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ModelDefaultRow>(); entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Credits).HasDefaultValue(5).HasSentinel(-1);
        entity.Property(row => row.Enabled).HasDefaultValue(true);
        entity.Property(row => row.Level).HasDefaultValue(ModelDefaultLevel.Standard).HasSentinel(ModelDefaultLevel.Unset);
        entity.Property(row => row.Label).HasDefaultValue("model");
        entity.Property(row => row.Score).HasDefaultValue(7).HasSentinel((int?)-1);
        entity.Property(row => row.Priority).HasDefaultValue((byte)10).HasSentinel(byte.MaxValue);
    }
}
