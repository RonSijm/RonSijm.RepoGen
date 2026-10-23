using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class MappedCollectionContext(DbContextOptions<MappedCollectionContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var parent = modelBuilder.Entity<MappedCollectionParent>();
        parent.Property(row => row.Id).ValueGeneratedNever();
        parent.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
        parent.HasMany(row => row.Children).WithOne().HasForeignKey(row => row.ParentId);
        var child = modelBuilder.Entity<MappedCollectionChild>();
        child.Property(row => row.Id).ValueGeneratedNever();
        child.HasIndex(row => new { row.ParentId, row.Code }).IsUnique();
        child.Property(row => row.Category).HasField("category").UsePropertyAccessMode(PropertyAccessMode.Field).HasDefaultValue("entry");
        child.Property<int>("audit").HasDefaultValue(10);
        child.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
        child.Property<int?>("OptionalVersion").HasDefaultValue(1).IsConcurrencyToken();
        child.IndexerProperty<byte[]?>("Stamp").HasDefaultValue(new byte[] { 1, 2 }).IsConcurrencyToken();
    }
}
