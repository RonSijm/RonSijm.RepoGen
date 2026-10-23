using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalReferences;

public sealed class OptionalAssignmentsContext(DbContextOptions<OptionalAssignmentsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredWorkspace>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredWorkspace>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredWorkspace>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<StoredSubject>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredSubject>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredOrigin>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredOrigin>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredOrigin>().HasAlternateKey(row => new { row.Partition, row.Code });
        modelBuilder.Entity<StoredAssignment>().HasKey(row => new { row.WorkspaceId, row.SubjectId });
        modelBuilder.Entity<StoredAssignment>().HasOne(row => row.Workspace).WithMany(row => row.Assignments).HasForeignKey(row => row.WorkspaceId).IsRequired();
        modelBuilder.Entity<StoredAssignment>().HasOne(row => row.Subject).WithMany(row => row.Assignments).HasForeignKey(row => row.SubjectId).IsRequired();
        modelBuilder.Entity<StoredAssignment>().HasOne(row => row.Origin).WithMany(row => row.Assignments)
            .HasForeignKey(row => new { row.OriginPartition, row.OriginCode }).HasPrincipalKey(row => new { row.Partition, row.Code }).IsRequired(false);
        modelBuilder.Entity<StoredAssignment>().Property(row => row.Version).IsConcurrencyToken();
    }
}
