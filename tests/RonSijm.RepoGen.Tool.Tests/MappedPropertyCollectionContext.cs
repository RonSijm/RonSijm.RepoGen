using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyCollectionContext(DbContextOptions<MappedPropertyCollectionContext> options) : DbContext(options)
{
    public DbSet<MappedPropertyOwner> Owners => Set<MappedPropertyOwner>();
    public DbSet<MappedPropertyChild> Children => Set<MappedPropertyChild>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MappedPropertyOwner>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<MappedPropertyOwner>().HasMany(row => row.Children).WithOne().HasForeignKey(row => row.OwnerId);
        modelBuilder.Entity<MappedPropertyChild>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<MappedPropertyChild>().Property<int>(MappedPropertyContext.Token).HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
    }
}
