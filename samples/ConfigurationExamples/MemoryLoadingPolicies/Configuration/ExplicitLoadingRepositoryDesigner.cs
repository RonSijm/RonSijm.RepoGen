using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryLoadingPolicies;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLoadingPolicies;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Choose model-independent loading per named operation, without a service-side query DSL.</summary>
public sealed class ExplicitLoadingRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryLoadingPolicies")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryLoadingPolicies")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryLoadingPolicies")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<StoredCatalogEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<CatalogEntrySeed, int>(row => row.Id, row => new(row.Id, row.GroupId, row.Name))
                .MapInMemoryEntity<CatalogEntrySeed>(row => new StoredCatalogEntry { Id = row.Id, GroupId = row.GroupId, Name = row.Name });
        });
        modelBuilder.Entity<StoredCatalogGroup>(entity =>
        {
            entity.WriteEnabled = false; entity.ProjectionEnabled = false;
            entity.BindReadPort<IExplicitCatalogReader>();
            entity.HasInMemoryStore<CatalogGroupSeed, int>(row => row.Id, row => new(row.Id, row.Name, row.Hidden))
                .MapInMemoryEntity<CatalogGroupSeed>(row => new StoredCatalogGroup { Id = row.Id, Name = row.Name, Hidden = row.Hidden });
            entity.HasInMemoryNavigation(row => row.Entries);
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            var entries = entity.HasLoadProfile("Entries", load => load.Include(row => row.Entries));
            entity.HasProjection<CatalogGroupCard>(row => new CatalogGroupCard(row.Id, row.Name));
            entity.HasProjection<CatalogGroupDetail>(row => new CatalogGroupDetail(row.Id, row.Name,
                row.Entries.OrderBy(entry => entry.Id).Select(entry => new CatalogEntryCard(entry.Id, entry.Name)).ToList()))
                .AutoIncludeNavigations(false); // RepoGen inference is separate from EF model AutoInclude.
            entity.HasQuery("Cards").IgnoreAutoIncludes().OrderBy(row => row.Id).ProjectTo<CatalogGroupCard>().List();
            entity.HasQuery("Detail").IgnoreAutoIncludes().Where<int>((row, id) => row.Id == id)
                .UseLoadProfile(entries).ProjectTo<CatalogGroupDetail>().SingleOrDefault();
            entity.HasQuery("Page").OrderBy(row => row.Id).Select(row => new CatalogGroupCard(row.Id, row.Name))
                .IgnoreAutoIncludes().Paged(); // The result builder sets the same whole-query policy.
            entity.HasQuery("Entries").From(sources => sources.Set<StoredCatalogGroup>().Join(sources.Set<StoredCatalogEntry>(),
                    group => group.Id, entry => entry.GroupId, (group, entry) => new { entry.Id, entry.Name })
                    .OrderBy(row => row.Id).Select(row => new CatalogEntryCard(row.Id, row.Name)))
                .IgnoreAutoIncludes().List(); // Applied to every Set root.
            entity.HasQuery("Stream").IgnoreAutoIncludes().OrderBy(row => row.Id).ProjectTo<CatalogGroupCard>().Stream();
        });
    }
}
