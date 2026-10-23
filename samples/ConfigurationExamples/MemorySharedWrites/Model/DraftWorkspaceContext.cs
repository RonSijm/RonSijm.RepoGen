using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedWrites;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedWrites;

public sealed class DraftWorkspaceContext(DbContextOptions<DraftWorkspaceContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Draft>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        });
        modelBuilder.Entity<JournalEntry>(entity => { entity.HasKey(row => row.Id); entity.Property(row => row.Id).ValueGeneratedNever(); });
    }
}
