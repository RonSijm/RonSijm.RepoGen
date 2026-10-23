// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Workboard;

public sealed class WorkboardContext(DbContextOptions<WorkboardContext> options) : DbContext(options)
{
    public DbSet<WorkItem> Items => Set<WorkItem>();
    public DbSet<WorkLabel> Labels => Set<WorkLabel>();
    public DbSet<WorkEvent> Events => Set<WorkEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkItem>().ToTable("WorkItems");
        modelBuilder.Entity<WorkLabel>().HasIndex(label => label.Name).IsUnique();
        modelBuilder.Entity<WorkSummary>().HasNoKey().ToView("Workload");
    }
}

public sealed class WorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool Completed { get; set; }
}

public sealed class WorkLabel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class WorkEvent
{
    public int Id { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class WorkSummary
{
    public int Priority { get; set; }
    public int Count { get; set; }
}
