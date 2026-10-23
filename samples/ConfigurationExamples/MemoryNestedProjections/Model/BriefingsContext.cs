using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNestedProjections;

public sealed class BriefingsContext(DbContextOptions<BriefingsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredBriefing>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredBriefing>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredBriefing>().Property(row => row.Revision).IsConcurrencyToken();
    }
}
