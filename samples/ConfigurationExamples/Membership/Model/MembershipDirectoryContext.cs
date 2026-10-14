// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Membership;

public sealed class DirectoryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class MembershipDirectoryContext(DbContextOptions<MembershipDirectoryContext> options) : DbContext(options)
{
    public DbSet<DirectoryItem> Items => Set<DirectoryItem>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<DirectoryItem>().Property(item => item.Id).ValueGeneratedNever();
}
