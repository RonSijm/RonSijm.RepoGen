using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryShadowReads;

public sealed class RankedNotebookContext(DbContextOptions<RankedNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StoredRankedNote>();
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property<int>("Rank");
        entity.Property<string>("Category").IsRequired();
        entity.Property<bool>("Deleted");
        entity.HasQueryFilter(row => !EF.Property<bool>(row, "Deleted"));
    }
}
