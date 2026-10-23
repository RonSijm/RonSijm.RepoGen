using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatches;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryBatches;

public sealed class BatchWorkspaceContext(DbContextOptions<BatchWorkspaceContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<BatchDocument>();
        document.HasKey(row => row.Id); // Ordinary EF generated-on-insert key; fixture rows supply already-known IDs.
        document.HasIndex(row => row.Code).IsUnique();
        document.Property(row => row.Version).IsConcurrencyToken();
    }
}
