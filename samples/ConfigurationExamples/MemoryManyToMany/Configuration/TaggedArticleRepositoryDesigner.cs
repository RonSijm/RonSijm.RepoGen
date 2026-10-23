using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryManyToMany;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryManyToMany;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Read a designed many-to-many projection through public scalar endpoint and join stores.</summary>
public sealed class TaggedArticleRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryManyToMany")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Generated.MemoryManyToMany")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryManyToMany")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<TaggedArticle>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ArticleRow, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Title))
                .MapInMemoryEntity<ArticleRow>(row => new TaggedArticle { TenantId = row.TenantId, Id = row.Id, Title = row.Title });
            entity.HasInMemoryNavigation(row => row.Tags);
            entity.HasQuery("Cards").OrderBy(row => row.TenantId).ThenBy(row => row.Id)
                .Select(row => new ArticleCard(row.TenantId, row.Id, row.Title, row.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList())).List();
            entity.HasQuery("PublicCards").OrderBy(row => row.TenantId).ThenBy(row => row.Id)
                .Select(row => new ArticleCard(row.TenantId, row.Id, row.Title, row.Tags.Where(tag => !tag.Hidden).OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList())).List();
            entity.HasQuery("WithTag").Where<string>((row, name) => row.Tags.Any(tag => tag.Name == name))
                .OrderBy(row => row.TenantId).ThenBy(row => row.Id).Select(row => row.Title).List();
        });
        modelBuilder.Entity<ArticleTag>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<TagRow, int>(row => row.Id, row => new(row.Id, row.Name, row.Hidden))
                .MapInMemoryEntity<TagRow>(row => new ArticleTag { Id = row.Id, Name = row.Name, Hidden = row.Hidden });
            entity.HasInMemoryNavigation(row => row.Articles);
            entity.HasVisibilityPolicy("Public", row => !row.Hidden);
            entity.HasQuery("Usage").OrderBy(row => row.Id).Select(row => new TagUsage(row.Name,
                row.Articles.OrderBy(article => article.TenantId).ThenBy(article => article.Id).Select(article => article.Title).ToList())).List();
        });
        modelBuilder.Entity<TaggedArticleLink>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ArticleTagRow, (int, int, int)>(row => new ValueTuple<int, int, int>(row.TenantId, row.ArticleId, row.TagId),
                    row => new(row.TenantId, row.ArticleId, row.TagId))
                .MapInMemoryEntity<ArticleTagRow>(row => new TaggedArticleLink { TenantId = row.TenantId, ArticleId = row.ArticleId, TagId = row.TagId });
        });
    }
}
