using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLoadingPolicies;

public sealed class AutoLoadedCatalogContext(DbContextOptions<AutoLoadedCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredCatalogGroup>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasMany(row => row.Entries).WithOne().HasForeignKey(row => row.GroupId);
            entity.Navigation(row => row.Entries).AutoInclude();
        });
        modelBuilder.Entity<StoredCatalogEntry>().Property(row => row.Id).ValueGeneratedNever();
    }
}
