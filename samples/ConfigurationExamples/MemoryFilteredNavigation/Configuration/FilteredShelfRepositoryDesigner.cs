using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredNavigation;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredNavigation;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Reuse scalar model filters before explicit collection/optional-reference fixture loading.</summary>
public sealed class FilteredShelfRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryFilteredNavigation")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryFilteredNavigation")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryFilteredNavigation")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<StoredFilteredShelf>(entity =>
        {
            entity.WriteEnabled = false; entity.BindReadPort<IFilteredShelfReader>();
            entity.HasInMemoryStore<FilteredShelfSeed, int>(row => row.Id, row => new(row.Id, row.Archived))
                .MapInMemoryEntity<FilteredShelfSeed>(row => new StoredFilteredShelf { Id = row.Id, Archived = row.Archived });
            entity.UseInMemoryModelQueryFilter(includeNavigations: true); // !Archived, including related reference reads.
            entity.HasInMemoryNavigation(row => row.Entries);
            entity.HasProjection<FilteredShelfCard>(row => new FilteredShelfCard(row.Id, row.Entries.Count,
                row.Entries.OrderBy(entry => entry.Id).Select(entry => entry.Title).ToList()));
            entity.HasQuery("Cards").OrderBy(row => row.Id).Load(load => load.Include(row => row.Entries)).ProjectTo<FilteredShelfCard>().List();
            entity.HasQuery("All").IgnoreQueryFilters().OrderBy(row => row.Id).ProjectTo<FilteredShelfCard>().List();
            entity.HasQuery("WithTitle").Where<string>((row, title) => row.Entries.Any(entry => entry.Title == title)).Select(row => row.Id).List();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<FilteredShelfCard>().Paged();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<FilteredShelfCard>().Stream();
        });
        modelBuilder.Entity<StoredFilteredEntry>(entity =>
        {
            entity.WriteEnabled = false; entity.BindReadPort<IFilteredEntryReader>();
            entity.HasInMemoryStore<FilteredEntrySeed, int>(row => row.Id, row => new(row.Id, row.ShelfId, row.Title, row.Deleted, row.Hidden))
                .MapInMemoryEntity<FilteredEntrySeed>(row => new StoredFilteredEntry { Id = row.Id, ShelfId = row.ShelfId, Title = row.Title, Deleted = row.Deleted, Hidden = row.Hidden });
            entity.UseInMemoryModelQueryFilter(includeNavigations: true); // !Deleted, including shelf collections.
            entity.HasInMemoryNavigation(row => row.Shelf);
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden); // Root-only; not injected into shelf collections.
            entity.HasProjection<FilteredEntryCard>(row => new FilteredEntryCard(row.Id, row.Title, row.Shelf == null ? null : row.Shelf.Id));
            entity.HasQuery("Cards").OrderBy(row => row.Id).ProjectTo<FilteredEntryCard>().List();
            entity.HasQuery("All").IgnoreQueryFilters().OrderBy(row => row.Id).ProjectTo<FilteredEntryCard>().List();
            entity.HasQuery("From").From(sources => sources.Set<StoredFilteredEntry>().OrderBy(row => row.Id)
                .Select(row => new FilteredEntryCard(row.Id, row.Title, row.Shelf == null ? null : row.Shelf.Id))).List();
        });
    }
}
