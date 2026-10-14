using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredNavigationRequiredContext(DbContextOptions<FilteredNavigationRequiredContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<ModelFilterRow>(); row.Property(item => item.Id).ValueGeneratedNever();
        row.HasQueryFilter(item => !item.Deleted);
        row.HasOne(item => item.Folder).WithMany(folder => folder.Rows).HasForeignKey(item => item.FolderId).IsRequired();
        var folder = modelBuilder.Entity<ModelFilterFolder>(); folder.Property(item => item.Id).ValueGeneratedNever();
        folder.HasQueryFilter(item => !item.Archived);
    }
}
