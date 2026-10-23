using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedKeys;

public sealed class KeyedNotebookContext(DbContextOptions<KeyedNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var folder = modelBuilder.Entity<KeyedFolder>();
        folder.Property(row => row.Id).ValueGeneratedNever();
        folder.Property(row => row.Code).HasField("code").UsePropertyAccessMode(PropertyAccessMode.Field);
        folder.HasAlternateKey(row => row.Code);
        var memo = modelBuilder.Entity<KeyedMemo>();
        memo.Property(row => row.Id).ValueGeneratedNever();
        memo.Property<string?>("FolderCode");
        memo.HasOne(row => row.Folder).WithMany(row => row.Memos).HasForeignKey("FolderCode").HasPrincipalKey(row => row.Code);
    }
}
