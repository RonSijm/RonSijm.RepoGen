using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadContext(DbContextOptions<ShadowReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<ShadowReadNote>();
        note.Property(row => row.Id).ValueGeneratedNever();
        note.Property<int>("Rank");
        note.Property<string>("Code").IsRequired();
        note.Property<int?>("Score");
        note.Property<bool>("Deleted");
        note.Property<ShadowReadStatus>("Status").HasConversion<string>();
        note.Property<byte[]?>("Fingerprint");
        note.Property<int>("tag.with\"quotes");
        note.HasQueryFilter(row => !EF.Property<bool>(row, "Deleted"));
        note.HasOne(row => row.Folder).WithMany(folder => folder.Notes).HasForeignKey(row => row.FolderId);
        var folder = modelBuilder.Entity<ShadowReadFolder>();
        folder.Property(row => row.Id).ValueGeneratedNever();
        folder.Property<bool>("Archived");
        folder.Property<int>("Rank");
        folder.HasQueryFilter(row => !EF.Property<bool>(row, "Archived"));
    }
}
