using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedReturning;

public sealed class ReceiptQueueContext(DbContextOptions<ReceiptQueueContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var envelope = modelBuilder.Entity<ReceiptEnvelope>();
        envelope.Property(row => row.Id).ValueGeneratedNever();
        envelope.Property(row => row.Category).HasField("category").UsePropertyAccessMode(PropertyAccessMode.Field).HasDefaultValue("notification");
        envelope.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
    }
}
