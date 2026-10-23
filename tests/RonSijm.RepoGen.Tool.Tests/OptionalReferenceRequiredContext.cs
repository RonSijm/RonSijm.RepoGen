using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceRequiredContext(DbContextOptions<OptionalReferenceContext> options) : OptionalReferenceContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OptionalReferenceRow>().HasOne(row => row.Source).WithMany(row => row.Links).HasForeignKey(row => row.SourceId).IsRequired();
    }
}
