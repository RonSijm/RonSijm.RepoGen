using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedCollectionSetters;

public sealed class ReviewBoardsContext(DbContextOptions<ReviewBoardsContext> options) : DbContext(options)
{
    public const string Revision = "revision";
    public const string Remark = "review remark";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var board = modelBuilder.Entity<ReviewBoard>();
        board.Property(row => row.Id).ValueGeneratedNever();
        board.Property<int>(Revision).IsConcurrencyToken();
        board.HasMany(row => row.Items).WithOne().HasForeignKey(row => row.BoardId);
        var item = modelBuilder.Entity<ReviewItem>();
        item.Property(row => row.Id).ValueGeneratedNever();
        item.HasIndex(row => new { row.BoardId, row.Code }).IsUnique();
        item.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field);
        item.Property<int>("edits").HasField("edits");
        item.Property<int>(Revision).IsConcurrencyToken();
        item.IndexerProperty<string?>(Remark);
    }
}
