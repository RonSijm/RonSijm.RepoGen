using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredLinks;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredLinks;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Reuse model filters on explicit endpoint/join stores, with independent saved-result consent.</summary>
public sealed class FilteredCatalogRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryFilteredLinks")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Generated.MemoryFilteredLinks")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryFilteredLinks")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries()
            .UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<FilteredArticle>(entity =>
        {
            entity.HasInMemoryStore<FilteredArticleSeed, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id),
                    row => new(row.TenantId, row.Id, row.Title, row.Archived))
                .MapInMemoryEntity<FilteredArticleSeed>(row => new FilteredArticle
                    { TenantId = row.TenantId, Id = row.Id, Title = row.Title, Archived = row.Archived })
                .MapInMemoryWrites<FilteredArticleSeed>((row, saved) => new(row.TenantId, row.Id, row.Title, row.Archived));
            entity.UseInMemoryModelQueryFilter(includeNavigations: true);
            entity.HasInMemoryNavigation(row => row.Labels);
            entity.HasProjection<FilteredArticleCard>(row => new FilteredArticleCard(row.TenantId, row.Id, row.Title,
                row.Labels.OrderBy(label => label.Id).Select(label => label.Name).ToList()));
            entity.HasQuery("Cards").OrderBy(row => row.TenantId).ThenBy(row => row.Id).ProjectTo<FilteredArticleCard>().List();
            entity.HasQuery("Page").OrderBy(row => row.TenantId).ThenBy(row => row.Id).ProjectTo<FilteredArticleCard>().Paged();
            entity.HasQuery("AllCards").IgnoreQueryFilters().OrderBy(row => row.TenantId).ThenBy(row => row.Id).ProjectTo<FilteredArticleCard>().List();
            entity.HasCommand("Rename").Where<RenameArticleInput>((row, input) => row.TenantId == input.TenantId && row.Id == input.Id)
                .Patch(setters => setters.SetProperty<string, RenameArticleInput>(row => row.Title, (row, input) => input.Title))
                .UseInMemoryModelQueryFilter(includeNavigations: true).Save()
                .ReturnsSaved(row => new FilteredArticleCard(row.TenantId, row.Id, row.Title,
                    row.Labels.OrderBy(label => label.Id).Select(label => label.Name).ToList()));
        });
        modelBuilder.Entity<FilteredLabel>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<FilteredLabelSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Archived, row.Hidden))
                .MapInMemoryEntity<FilteredLabelSeed>(row => new FilteredLabel
                    { Id = row.Id, Name = row.Name, Archived = row.Archived, Hidden = row.Hidden });
            entity.UseInMemoryModelQueryFilter(includeNavigations: true);
            entity.HasInMemoryNavigation(row => row.Articles);
            entity.HasVisibilityPolicy("Public", row => !row.Hidden);
            entity.HasQuery("Usage").OrderBy(row => row.Id).Select(row => new FilteredLabelUsage(row.Name,
                row.Articles.OrderBy(article => article.TenantId).ThenBy(article => article.Id).Select(article => article.Title).ToList())).List();
        });
        modelBuilder.Entity<FilteredArticleLabel>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<FilteredLinkSeed, int>(row => row.Id, row => new(row.Id, row.TenantId, row.ArticleId, row.LabelId, row.Archived))
                .MapInMemoryEntity<FilteredLinkSeed>(row => new FilteredArticleLabel
                    { Id = row.Id, TenantId = row.TenantId, ArticleId = row.ArticleId, LabelId = row.LabelId, Archived = row.Archived });
            entity.UseInMemoryModelQueryFilter(includeNavigations: true);
        });
    }
}
