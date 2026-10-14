using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryRelationshipComparers;

public sealed class ComparedDirectoryContext(DbContextOptions<ComparedDirectoryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredFolder>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredFolder>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredFolder>().HasAlternateKey(row => new { row.Region, row.Code });
        modelBuilder.Entity<StoredFolderEntry>().HasKey(row => row.Id);
        modelBuilder.Entity<StoredFolderEntry>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredFolder>().HasMany(row => row.Entries).WithOne(row => row.Folder)
            .HasForeignKey(row => new { row.Region, row.FolderCode }).HasPrincipalKey(row => new { row.Region, row.Code }).IsRequired(false);
        var comparer = new ValueComparer<string>(
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            value => StringComparer.OrdinalIgnoreCase.GetHashCode(value), value => value);
        // Production settings remain explicit; a fixture policy does not rewrite CLR or SQL equality.
        modelBuilder.Entity<StoredFolder>().Property(row => row.Code).UseCollation("NOCASE").Metadata.SetValueComparer(comparer);
        modelBuilder.Entity<StoredFolderEntry>().Property(row => row.FolderCode).UseCollation("NOCASE").Metadata.SetValueComparer(comparer);
    }
}
