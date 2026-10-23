using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReconciliationContext(DbContextOptions<MappedReconciliationContext> options) : DbContext(options)
{
    public const string Stamp = "Stamp ] \"; not SQL --";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var parent = modelBuilder.Entity<MappedCollectionParent>();
        parent.Property(row => row.Id).ValueGeneratedNever();
        parent.Property<int>("Revision").IsConcurrencyToken();
        parent.HasMany(row => row.Children).WithOne().HasForeignKey(row => row.ParentId);
        var child = modelBuilder.Entity<MappedCollectionChild>();
        child.Property(row => row.Id).ValueGeneratedNever();
        child.HasIndex(row => new { row.ParentId, row.Code }).IsUnique();
        child.Property(row => row.Category).HasField("category").UsePropertyAccessMode(PropertyAccessMode.Field);
        child.Property<int>("audit").HasField("audit");
        child.Property<int>("Revision").IsConcurrencyToken();
        child.Property<int?>("OptionalVersion").IsConcurrencyToken();
        child.IndexerProperty<byte[]?>(Stamp).IsConcurrencyToken();
    }
}
