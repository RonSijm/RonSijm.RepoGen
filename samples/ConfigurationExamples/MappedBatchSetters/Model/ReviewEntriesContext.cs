using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedBatchSetters;

public sealed class ReviewEntriesContext(DbContextOptions<ReviewEntriesContext> options) : DbContext(options)
{
    public const string Revision = "revision";
    public const string Remark = "review remark";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<ReviewEntry>();
        entry.Property(row => row.Id).ValueGeneratedNever();
        entry.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field);
        entry.Property<int>("edits").HasField("edits");
        entry.Property<int>(Revision).IsConcurrencyToken();
        entry.IndexerProperty<string?>(Remark);
    }
}
