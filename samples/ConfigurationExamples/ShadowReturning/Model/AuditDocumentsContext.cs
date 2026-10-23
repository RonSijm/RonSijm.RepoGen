using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.ShadowReturning;

public sealed class AuditDocumentsContext(DbContextOptions<AuditDocumentsContext> options) : DbContext(options)
{
    public DbSet<AuditDocument> Documents => Set<AuditDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<AuditDocument>();
        document.Property(row => row.Revision).IsConcurrencyToken();
        document.Property<string>("NormalizedTitle").HasComputedColumnSql("upper(\"Title\")", stored: true).IsRequired();
        document.Property<int>("TitleLength").HasComputedColumnSql("length(\"Title\")", stored: true);
        document.Property<string?>("SourceTag").HasDefaultValue("database");
        document.Property<bool>("Archived").HasDefaultValue(false);
        document.HasQueryFilter(row => !EF.Property<bool>(row, "Archived"));
    }
}
