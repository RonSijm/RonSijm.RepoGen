// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.TaskDesk;

public sealed class DeskTask
{
    public int Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string? Tag { get; set; }
}

public sealed class TaskDeskContext(DbContextOptions<TaskDeskContext> options) : DbContext(options)
{
    public DbSet<DeskTask> Tasks => Set<DeskTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<DeskTask>().HasIndex(task => new { task.ProjectId, task.Id });
}
