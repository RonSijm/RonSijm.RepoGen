using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProjectionHelpers;

public sealed class ProjectionMemosContext(DbContextOptions<ProjectionMemosContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredProjectionMemo>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredProjectionMemo>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredProjectionMemo>().Property(row => row.Revision).IsConcurrencyToken();
    }
}
