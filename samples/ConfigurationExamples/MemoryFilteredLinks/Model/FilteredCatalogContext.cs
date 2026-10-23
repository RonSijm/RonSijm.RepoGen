using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredLinks;

public sealed class FilteredCatalogContext(DbContextOptions<FilteredCatalogContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilteredArticle>().HasKey(row => new { row.TenantId, row.Id });
        modelBuilder.Entity<FilteredArticle>().HasQueryFilter(row => !row.Archived);
        modelBuilder.Entity<FilteredLabel>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<FilteredLabel>().HasQueryFilter(row => !row.Archived);
        modelBuilder.Entity<FilteredArticle>().HasMany(row => row.Labels).WithMany(row => row.Articles).UsingEntity<FilteredArticleLabel>(
            right => right.HasOne<FilteredLabel>().WithMany().HasForeignKey(row => row.LabelId),
            left => left.HasOne<FilteredArticle>().WithMany().HasForeignKey(row => new { row.TenantId, row.ArticleId }),
            join =>
            {
                join.Property(row => row.Id).ValueGeneratedNever();
                join.HasIndex(row => new { row.TenantId, row.ArticleId, row.LabelId }).IsUnique();
                join.HasQueryFilter(row => !row.Archived);
            });
    }
}
