using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicResults;

public sealed class EnrollmentContext(DbContextOptions<EnrollmentContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>().Property(row => row.Id).ValueGeneratedNever();
        var enrollment = modelBuilder.Entity<Enrollment>();
        enrollment.Property(row => row.Id).ValueGeneratedNever();
        enrollment.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        enrollment.HasIndex(row => new { row.CourseId, row.Code }).IsUnique();
        enrollment.HasOne(row => row.Course).WithMany(row => row.Enrollments).HasForeignKey(row => row.CourseId);
    }
}
