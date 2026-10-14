using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionNestedContext(DbContextOptions<CompositeCollectionNestedContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompositeCollectionWideOwner>().Property(row => row.Id).ValueGeneratedNever();
        var child = modelBuilder.Entity<CompositeCollectionWideChild>();
        child.Ignore(row => row.NullablePackedKey);
        child.Property(row => row.Id).ValueGeneratedNever();
        child.Property(row => row.PackedKey).HasConversion(value => value.Item1, value => new ValueTuple<string, string>(value, ""));
        child.HasIndex(row => row.PackedKey).IsUnique();
        child.HasOne<CompositeCollectionWideOwner>().WithMany(row => row.Children).HasForeignKey(row => row.OwnerId);
    }
}
