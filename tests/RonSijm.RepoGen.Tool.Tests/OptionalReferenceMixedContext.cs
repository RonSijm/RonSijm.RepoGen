using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OptionalReferenceMixedContext(DbContextOptions<OptionalReferenceContext> options) : OptionalReferenceContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OptionalReferenceRow>().Property(row => row.ReviewerCode).IsRequired();
    }
}
