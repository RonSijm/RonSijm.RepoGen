using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.CompositeCollections;

public sealed class TranslationCatalogContext(DbContextOptions<TranslationCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<LocalizedDocument>();
        document.HasKey(row => new { row.TenantId, row.Id });
        document.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        var text = modelBuilder.Entity<LocalizedText>();
        text.HasOne(row => row.Document).WithMany(row => row.Texts).HasForeignKey(row => new { row.TenantId, row.DocumentId });
        text.HasIndex(row => new { row.TenantId, row.DocumentId, row.Code, row.Locale }).IsUnique();
        text.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
    }
}
