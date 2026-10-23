using RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyShelves;
using RonSijm.RepoGen.ConfigurationExamples.Models.ProxyShelves;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ProxyShelfRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ProxyShelves")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.ProxyShelves")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ProxyShelves")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries()
            .RequireNavigationVisibilityReview().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<ShelfCategoryRecord>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ShelfCategorySeed, int>(row => row.Key, row => new ShelfCategorySeed(row.Key, row.Caption))
                .MapInMemoryEntity<ShelfCategorySeed>(row => new ShelfCategoryRecord { Id = row.Key, Name = row.Caption });
        });
        modelBuilder.Entity<ShelfItemRecord>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ShelfItemSeed, int>(row => row.Key,
                    row => new ShelfItemSeed(row.Key, row.ShelfCode, row.ShelfRegion, row.CategoryKey, row.Caption, row.Quantity, row.Hidden))
                .MapInMemoryEntity<ShelfItemSeed>(row => new ShelfItemRecord
                { Id = row.Key, ShelfCode = row.ShelfCode, ShelfRegion = row.ShelfRegion, CategoryId = row.CategoryKey, Name = row.Caption, Quantity = row.Quantity, Hidden = row.Hidden });
            entity.HasInMemoryNavigation(row => row.Shelf).HasInMemoryNavigation(row => row.Category);
            entity.HasVisibilityPolicy("Items", row => !row.Hidden);
        });
        modelBuilder.Entity<ShelfRecord>(entity =>
        {
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            entity.BindReadPort<IShelfCatalog>();
            entity.HasInMemoryStore<ShelfSeed, int>(row => row.Key, row => new ShelfSeed(row.Key, row.Code, row.Region, row.Caption, row.Hidden))
                .MapInMemoryEntity<ShelfSeed>(row => new ShelfRecord { Id = row.Key, Code = row.Code, Region = row.Region, Name = row.Caption, Hidden = row.Hidden });
            entity.HasInMemoryNavigation(row => row.Items);
            entity.HasVisibilityPolicy("Shelves", row => !row.Hidden);
            entity.ReviewNavigationVisibility<ShelfItemRecord>("Items", "Browse explicitly filters hidden children; RawCounts deliberately counts them too.");
            entity.HasProjection(row => new ShelfCard(row.Id, row.Name, row.Items.Where(item => !item.Hidden).OrderBy(item => item.Id)
                .Select(item => new ShelfItemCard(item.Id, item.Name, item.Quantity, item.Category == null ? null : item.Category.Name, null)).ToList()));
            var full = entity.HasLoadProfile("Full", load => load.Include(row => row.Items).ThenInclude(item => item.Category));
            entity.HasQuery("Browse").OrderBy(row => row.Id).ProjectTo<ShelfCard>().List();
            entity.HasQuery("Detail").Where<int>((row, id) => row.Id == id).UseLoadProfile(full).ProjectTo<ShelfCard>().SingleOrDefault();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<ShelfCard>().Paged();
            entity.HasQuery("RawCounts").OrderBy(row => row.Id).Select(row => row.Items.Count).List();
            entity.HasQuery("Items").From(sources => sources.Set<ShelfItemRecord>().OrderBy(item => item.Id)
                .Select(item => new ShelfItemCard(item.Id, item.Name, item.Quantity, item.Category == null ? null : item.Category.Name,
                    item.Shelf == null ? null : item.Shelf.Name)))
                .ReviewNavigationVisibility<ShelfRecord>("Shelves", "The item-root query deliberately includes hidden related shelf names.").List();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<ShelfCard>().Stream();
        });
    }
}
