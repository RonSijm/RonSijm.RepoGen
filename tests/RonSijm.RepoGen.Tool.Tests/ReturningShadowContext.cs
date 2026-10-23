using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowContext(DbContextOptions<ReturningShadowContext> options, string shape = "normal") : DbContext(options)
{
    public string Shape { get; } = shape;
    public DbSet<ReturningShadowRow> Rows => Set<ReturningShadowRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReturningShadowRow>();
        entity.ToTable("returning \" shadow", table => table.HasCheckConstraint("positive_quantity", "\"Quantity\" >= 0"));
        entity.HasKey(row => new { row.TenantId, row.Id });
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Revision).IsConcurrencyToken();
        entity.Property<string>("Scope").HasColumnName("scope \" quoted").HasDefaultValue("alpha");
        entity.Property<string>("Normalized").HasComputedColumnSql("upper(\"Text\")", stored: true);
        entity.Property<int>("ComputedVersion").HasComputedColumnSql("\"Quantity\" + 10", stored: true).IsConcurrencyToken();
        entity.Property<int>("Credit").HasDefaultValue(7);
        entity.Property<ReturningState?>("Status").HasConversion<string>();
        entity.Property<string?>("OptionalNote").HasColumnName("note ] \"; ignored --").IsRequired(Shape == "lyingNull");
        entity.Property<byte[]?>("Fingerprint");
        entity.HasQueryFilter(row => EF.Property<string>(row, "Scope") == "alpha");
        if (Shape == "field") entity.Property<int>("audit").HasField("audit");
        if (Shape == "indexer") entity.IndexerProperty<string>("Extra");
        if (Shape == "trigger") entity.ToTable(table => table.HasTrigger("declared_trigger"));
        if (Shape == "shadowKey") entity.HasKey("Scope", nameof(ReturningShadowRow.Id));
    }
}
