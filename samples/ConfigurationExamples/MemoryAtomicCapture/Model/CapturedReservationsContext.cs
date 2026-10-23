using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicCapture;

public sealed class CapturedReservationsContext(DbContextOptions<CapturedReservationsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CapturedReservation>();
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.HasIndex(row => row.Code).IsUnique();
        entity.Property(row => row.Label).HasField("label").UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasComputedColumnSql("upper(\"Code\")", stored: true);
        entity.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
        entity.IndexerProperty<string?>("Route").HasDefaultValue("queued");
    }
}
