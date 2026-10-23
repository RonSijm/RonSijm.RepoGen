using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowSqlContext(string connection, string table) : DbContext
{
    public string Table { get; } = table;
    public DbSet<ReturningShadowRow> Rows => Set<ReturningShadowRow>();
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlServer(connection)
        .ReplaceService<IModelCacheKeyFactory, ReturningShadowSqlModelCacheKeyFactory>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReturningShadowRow>(); entity.ToTable(Table, "dbo");
        entity.HasKey(row => new { row.TenantId, row.Id }); entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Revision).IsConcurrencyToken();
        entity.Property<string>("Scope").HasColumnName("scope]quoted").HasDefaultValue("alpha");
        entity.Property<string>("Normalized").HasComputedColumnSql("UPPER([Text])").IsRequired();
        entity.Property<int>("ComputedVersion").HasComputedColumnSql("[Quantity] + 10").IsConcurrencyToken();
        entity.Property<int>("Credit").HasDefaultValue(7);
        entity.Property<ReturningState?>("Status").HasConversion<string>();
        entity.Property<string?>("OptionalNote").HasColumnName("note]quoted");
        entity.Property<byte[]>("Fingerprint").HasColumnName("shadow]version").IsRowVersion();
        entity.HasQueryFilter(row => EF.Property<string>(row, "Scope") == "alpha");
    }
}
