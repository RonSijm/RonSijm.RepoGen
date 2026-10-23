using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLinks;

public sealed class BoardLabelsContext(DbContextOptions<BoardLabelsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredBoard>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredBoard>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<StoredLabel>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<StoredBoard>().HasMany(row => row.Labels).WithMany(row => row.Boards).UsingEntity<StoredBoardLabel>(
            target => target.HasOne(row => row.Label).WithMany(row => row.Links).HasForeignKey(row => row.LabelId).IsRequired(),
            owner => owner.HasOne(row => row.Board).WithMany(row => row.Links).HasForeignKey(row => row.BoardId).IsRequired(),
            link => { link.HasKey(row => new { row.BoardId, row.LabelId }); link.Property(row => row.Version).IsConcurrencyToken(); });
    }
}
