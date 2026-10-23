using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryContextFilters;

public sealed class TenantNotebookContext(DbContextOptions<TenantNotebookContext> options, string tenantId = "design-time") : DbContext(options)
{
    public string TenantId { get; } = tenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantNote>(entity =>
        {
            entity.Property(note => note.Id).ValueGeneratedNever();
            entity.HasQueryFilter(note => note.TenantId == TenantId && !note.Deleted);
        });
    }
}
