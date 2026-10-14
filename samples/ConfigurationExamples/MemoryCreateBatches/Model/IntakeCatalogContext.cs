using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCreateBatches;

public sealed class IntakeCatalogContext(DbContextOptions<IntakeCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntakeFolder>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<IntakeEntry>().HasOne(row => row.Folder).WithMany(row => row.Entries).HasForeignKey(row => row.FolderId).IsRequired();
    }
}
