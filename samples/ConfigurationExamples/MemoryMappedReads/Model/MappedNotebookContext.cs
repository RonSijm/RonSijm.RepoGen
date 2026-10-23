using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedReads;

public sealed class MappedNotebookContext(DbContextOptions<MappedNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StoredMappedMemo>();
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.Property<int>("revision").HasField("revision").IsConcurrencyToken();
        entity.Property<bool>("deleted").HasField("deleted");
        entity.IndexerProperty<string>("Source").IsRequired();
        entity.HasQueryFilter(row => !EF.Property<bool>(row, "deleted"));
    }
}
