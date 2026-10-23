using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemorySkipWriteContext(DbContextOptions<MemorySkipWriteContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var owner = modelBuilder.Entity<NavigationBoundaryOwner>();
        owner.Ignore(row => row.ReadOnly).Ignore(row => row.InitOnly).Ignore(row => row.ShadowReference)
            .Ignore(row => row.ConvertedReference).Ignore(row => row.CustomCollection);
        owner.HasKey(row => row.Id);
        owner.Property(row => row.Id).ValueGeneratedNever();
        // Many-to-many skip navigations are not returned by ordinary GetNavigations().
        owner.HasMany(row => row.Tags).WithMany(row => row.Owners);
        owner.Navigation(row => row.Tags).AutoInclude();
        modelBuilder.Entity<NavigationBoundaryTarget>().Property(row => row.Id).ValueGeneratedNever();
    }
}
