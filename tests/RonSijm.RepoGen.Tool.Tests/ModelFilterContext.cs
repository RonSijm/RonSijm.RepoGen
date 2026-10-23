using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterContext(DbContextOptions<ModelFilterContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelFilterRow>(entity =>
        {
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasQueryFilter(row => !row.Deleted);
            entity.HasOne(row => row.Folder).WithMany(folder => folder.Rows).HasForeignKey(row => row.FolderId);
        });
        modelBuilder.Entity<ModelFilterFolder>(entity =>
        {
            entity.Property(folder => folder.Id).ValueGeneratedNever();
            entity.HasQueryFilter(folder => !folder.Archived);
        });
    }
}
