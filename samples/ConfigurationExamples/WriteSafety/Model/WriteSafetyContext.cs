using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.WriteSafety;

public sealed class WriteSafetyContext(DbContextOptions<WriteSafetyContext> options) : DbContext(options)
{
    public DbSet<AuditedNote> Notes => Set<AuditedNote>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditedNote>().Property(note => note.Version).IsConcurrencyToken();
    }
}
