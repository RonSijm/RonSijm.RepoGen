// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Reporting;

public sealed class Sale
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public sealed class RegionLabel
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
}

public sealed class ReportingContext(DbContextOptions<ReportingContext> options) : DbContext(options)
{
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<RegionLabel> RegionLabels => Set<RegionLabel>();
    public int DisposeCount { get; private set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<RegionLabel>().HasIndex(label => label.Region).IsUnique();

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        return base.DisposeAsync();
    }
}
