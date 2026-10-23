using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicWrites;

public sealed class ReservationDirectoryContext(DbContextOptions<ReservationDirectoryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReservationSlot>();
        entity.HasIndex(row => new { row.Scope, row.Code }).IsUnique();
        entity.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
    }
}
