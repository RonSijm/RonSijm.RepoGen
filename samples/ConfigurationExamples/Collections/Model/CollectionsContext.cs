using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Collections;

public sealed class CollectionsContext(DbContextOptions<CollectionsContext> options) : DbContext(options)
{
    public DbSet<Checklist> Checklists => Set<Checklist>();
    public DbSet<ChecklistItem> Items => Set<ChecklistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Checklist>().Property(list => list.Version).IsConcurrencyToken();
        modelBuilder.Entity<ChecklistItem>().HasIndex(item => new { item.ChecklistId, item.Key }).IsUnique();
    }
}
