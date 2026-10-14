using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedGraphFilterPlainContext(DbContextOptions<SavedGraphFilterPlainContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelFilterRow>().Property(item => item.Id).ValueGeneratedNever();
        modelBuilder.Entity<ModelFilterFolder>().Property(item => item.Id).ValueGeneratedNever();
        modelBuilder.Entity<ModelFilterRow>().HasOne(item => item.Folder).WithMany(folder => folder.Rows).HasForeignKey(item => item.FolderId);
    }
}
