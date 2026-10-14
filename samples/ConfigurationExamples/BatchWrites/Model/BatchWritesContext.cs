using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.BatchWrites;

public sealed class BatchWritesContext(DbContextOptions<BatchWritesContext> options) : DbContext(options)
{
    public DbSet<BatchNote> Notes => Set<BatchNote>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BatchNote>().HasIndex(note => note.Code).IsUnique();
        modelBuilder.Entity<BatchNote>().Property(note => note.Version).IsConcurrencyToken();
    }
}
