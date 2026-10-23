// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.TaskDesk;

// A separate tiny model demonstrates accessor adaptation on a public CQRS-selector surface.
public readonly record struct StorageDirectoryId(Guid Value);

public sealed class DirectoryEntry
{
    public StorageDirectoryId Id { get; set; }
    public int Code { get; set; }
    public string Name { get; set; } = "";
}

public sealed class LookupDirectoryContext(DbContextOptions<LookupDirectoryContext> options) : DbContext(options)
{
    public DbSet<DirectoryEntry> Entries => Set<DirectoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DirectoryEntry>().Property(entry => entry.Id)
            .HasConversion(value => value.Value, value => new StorageDirectoryId(value)).ValueGeneratedNever();
        modelBuilder.Entity<DirectoryEntry>().HasIndex(entry => new { entry.Code, entry.Id }).IsUnique();
    }
}
