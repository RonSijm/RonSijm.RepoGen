using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNavigation;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class NavigationCatalogRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryNavigation")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryNavigation")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNavigation")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries()
            .RequireNavigationVisibilityReview().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<CatalogCategory>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new CatalogCategory { Id = row.Id, Name = row.Name });
        });
        modelBuilder.Entity<CatalogEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new CatalogEntry
            {
                Id = row.Id, GroupCode = row.GroupCode, GroupRegion = row.GroupRegion, CategoryId = row.CategoryId,
                Title = row.Title, Quantity = row.Quantity, Hidden = row.Hidden
            });
            entity.HasInMemoryNavigation(row => row.Group).HasInMemoryNavigation(row => row.Category);
            entity.HasVisibilityPolicy("Entries", row => !row.Hidden);
            entity.ReviewNavigationVisibility<CatalogGroup>("Groups", "Items deliberately names its hidden group; the root Entries policy still applies.");
        });
        modelBuilder.Entity<CatalogGroup>(entity =>
        {
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            entity.BindReadPort<ICatalogBrowse>();
            entity.HasInMemoryStore(row => row.Id, row => new CatalogGroup
                { Id = row.Id, Code = row.Code, Region = row.Region, Name = row.Name, Hidden = row.Hidden });
            entity.HasInMemoryNavigation(row => row.Entries);
            entity.HasVisibilityPolicy("Groups", row => !row.Hidden);
            entity.ReviewNavigationVisibility<CatalogEntry>("Entries", "Cards explicitly filter hidden entries; RawCounts deliberately counts every related entry.");
            entity.HasProjection(group => new CatalogGroupCard(group.Id, group.Name,
                group.Entries.Where(entry => !entry.Hidden).Sum(entry => entry.Quantity),
                group.Entries.Where(entry => !entry.Hidden).OrderBy(entry => entry.Id)
                    .Select(entry => new CatalogEntryCard(entry.Id, entry.Title, entry.Category == null ? null : entry.Category.Name, null)).ToList()));
            var full = entity.HasLoadProfile("Full", load => load.Include(group => group.Entries).ThenInclude(entry => entry.Category));
            entity.HasQuery("Browse").OrderBy(group => group.Id).ProjectTo<CatalogGroupCard>().List();
            entity.HasQuery("Detail").Where<int>((group, id) => group.Id == id).UseLoadProfile(full).ProjectTo<CatalogGroupCard>().SingleOrDefault();
            entity.HasQuery("Page").OrderBy(group => group.Id).ProjectTo<CatalogGroupCard>().Paged();
            entity.HasQuery("RawCounts").OrderBy(group => group.Id).Select(group => group.Entries.Count).List();
            entity.HasQuery("Items").From(sources => sources.Set<CatalogEntry>().OrderBy(entry => entry.Id)
                .Select(entry => new CatalogEntryCard(entry.Id, entry.Title, entry.Category == null ? null : entry.Category.Name,
                    entry.Group == null ? null : entry.Group.Name)))
                .ReviewNavigationVisibility<CatalogGroup>("Groups", "The replacement entry root does not implicitly filter its navigation to groups.").List();
            entity.HasQuery("Stream").OrderBy(group => group.Id).ProjectTo<CatalogGroupCard>().Stream();
        });
    }
}
