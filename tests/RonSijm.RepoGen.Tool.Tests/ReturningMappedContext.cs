using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedContext(DbContextOptions<ReturningMappedContext> options, string shape = "normal", string? table = null) : DbContext(options)
{
    public const string Note = "note ] \"; ignored --";
    public string Shape { get; } = shape;
    public string Table { get; } = table ?? "mapped ] \" values";
    public DbSet<ReturningMappedRow> Rows => Set<ReturningMappedRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReturningMappedRow>();
        entity.ToTable(Table);
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasComputedColumnSql("upper([Title])", stored: true);
        entity.Property<int>("audit").HasField("audit").HasColumnName("audit \" column")
            .HasComputedColumnSql("[Quantity] + 10", stored: true).IsConcurrencyToken();
        entity.Property<ReturningState?>("state").HasField("state").HasConversion<string>();
        entity.Property<byte[]?>("fingerprint").HasField("fingerprint");
        entity.IndexerProperty<string?>(Note).HasColumnName("note ] \" column");
        entity.Property<bool>("Archived").HasDefaultValue(false);
        entity.HasQueryFilter(row => !EF.Property<bool>(row, "Archived"));
        if (Shape == "readonly") entity.Property<int>("immutable").HasField("immutable");
        if (Shape == "fieldKey") entity.HasKey("audit", nameof(ReturningMappedRow.Id));
        if (Shape == "indexerKey") { entity.IndexerProperty<int>("IndexKey"); entity.HasKey("IndexKey", nameof(ReturningMappedRow.Id)); }
        if (Shape == "trigger") entity.ToTable(table => table.HasTrigger("declared_trigger"));
    }
}
