using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterContext(DbContextOptions<ContextFilterContext> options, bool throwOnRead = true) : DbContext(options)
{
    public ContextFilterKey RuntimeKey { get; set; } = new("A");
    public string Tenant => throwOnRead ? throw new InvalidOperationException("Generation must not read Tenant.") : RuntimeKey.Tenant;
    public int FolderId => throwOnRead ? throw new InvalidOperationException("Generation must not read FolderId.") : RuntimeKey.FolderId;
    public bool IncludeDeleted => throwOnRead ? throw new InvalidOperationException("Generation must not read IncludeDeleted.") : RuntimeKey.IncludeDeleted;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelFilterRow>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Ignore(row => row.Unmapped);
            entity.HasQueryFilter(row => row.Title == Tenant && (IncludeDeleted || !row.Deleted));
            entity.HasOne(row => row.Folder).WithMany(folder => folder.Rows).HasForeignKey(row => row.FolderId);
        });
        modelBuilder.Entity<ModelFilterFolder>(entity =>
        {
            entity.Property(folder => folder.Id).ValueGeneratedNever();
            entity.HasQueryFilter(folder => folder.Id == FolderId && !folder.Archived);
        });
    }
}
