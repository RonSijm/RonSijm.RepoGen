using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionWideContext(DbContextOptions<CompositeCollectionWideContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompositeCollectionWideOwner>().Property(row => row.Id).ValueGeneratedNever();
        var child = modelBuilder.Entity<CompositeCollectionWideChild>();
        child.Ignore(row => row.PackedKey);
        child.Ignore(row => row.NullablePackedKey);
        child.Property(row => row.Id).ValueGeneratedNever();
        child.HasOne<CompositeCollectionWideOwner>().WithMany(row => row.Children).HasForeignKey(row => row.OwnerId);
        child.HasIndex(row => new { row.OwnerId, row.Key1 }).IsUnique();
    }
}
