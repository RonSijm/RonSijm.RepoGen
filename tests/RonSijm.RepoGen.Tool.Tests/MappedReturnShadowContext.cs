using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReturnShadowContext(DbContextOptions<MappedReturnShadowContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MappedReturnShadowRow>();
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property<int>("Revision").HasComputedColumnSql("\"Amount\" + 1", stored: true).IsConcurrencyToken();
        entity.HasQueryFilter(row => EF.Property<int>(row, "Revision") < 5);
    }
}
