using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryManyToMany;

public sealed class TaggedArticleContext(DbContextOptions<TaggedArticleContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaggedArticle>().HasKey(row => new { row.TenantId, row.Id });
        modelBuilder.Entity<ArticleTag>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<TaggedArticle>().HasMany(row => row.Tags).WithMany(row => row.Articles).UsingEntity<TaggedArticleLink>(
            right => right.HasOne<ArticleTag>().WithMany().HasForeignKey(row => row.TagId),
            left => left.HasOne<TaggedArticle>().WithMany().HasForeignKey(row => new { row.TenantId, row.ArticleId }),
            join => join.HasKey(row => new { row.TenantId, row.ArticleId, row.TagId }));
    }
}
