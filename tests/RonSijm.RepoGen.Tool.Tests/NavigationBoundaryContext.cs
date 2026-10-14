using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationBoundaryContext(DbContextOptions<NavigationBoundaryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var owner = modelBuilder.Entity<NavigationBoundaryOwner>();
        owner.HasOne(row => row.ReadOnly).WithMany().HasForeignKey("ReadOnlyId");
        owner.HasOne(row => row.InitOnly).WithMany().HasForeignKey("InitOnlyId");
        owner.HasOne(row => row.ShadowReference).WithMany().HasForeignKey("ShadowId");
        owner.HasOne(row => row.ConvertedReference).WithMany().HasForeignKey(row => row.Code).HasPrincipalKey(row => row.Code);
        owner.Property(row => row.Code).HasConversion(value => value == null ? null : value.ToLowerInvariant(), value => value);
        owner.HasMany(row => row.CustomCollection).WithOne().HasForeignKey(row => row.OwnerId);
        owner.HasMany(row => row.Tags).WithMany(row => row.Owners);
        modelBuilder.Entity<NavigationBoundaryTarget>().Property(row => row.Code).HasConversion(value => value.ToLowerInvariant(), value => value);
        modelBuilder.Entity<NavigationBoundaryTarget>().HasQueryFilter(row => !row.Hidden);
    }
}
