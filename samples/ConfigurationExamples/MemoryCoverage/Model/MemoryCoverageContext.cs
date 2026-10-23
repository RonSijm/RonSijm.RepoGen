using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCoverage;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCoverage;

public sealed class MemoryCoverageContext(DbContextOptions<MemoryCoverageContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<ReviewedEntry>().HasQueryFilter(row => !row.Hidden);
}
