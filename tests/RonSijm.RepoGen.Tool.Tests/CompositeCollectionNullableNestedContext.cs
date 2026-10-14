using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionNullableNestedContext(DbContextOptions<CompositeCollectionNullableNestedContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompositeCollectionWideOwner>().Property(row => row.Id).ValueGeneratedNever();
        var child = modelBuilder.Entity<CompositeCollectionWideChild>();
        child.Ignore(row => row.PackedKey);
        child.Property(row => row.Id).ValueGeneratedNever();
        child.Property(row => row.NullablePackedKey)
            .HasConversion(value => value!.Value.Item1, value => (ValueTuple<string, string>?)new ValueTuple<string, string>(value, ""))
            .IsRequired();
        child.HasIndex(row => row.NullablePackedKey).IsUnique();
        child.HasOne<CompositeCollectionWideOwner>().WithMany(row => row.Children).HasForeignKey(row => row.OwnerId);
    }
}
