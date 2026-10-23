using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryImmediateWrites;

public sealed class DispatchQueueContext(DbContextOptions<DispatchQueueContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<QueuedEnvelope>();
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
    }
}
