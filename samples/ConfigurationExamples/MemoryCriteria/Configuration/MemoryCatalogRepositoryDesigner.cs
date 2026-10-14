// Designer -> ../Model/MemoryCatalogContext.cs -> ../Generated/TestDoubles.g.cs and shared closed ports.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCatalog;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class MemoryCatalogRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryCatalog")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryCatalog")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCatalog")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries()
            .UseKeyedContextLeases<CatalogPartition>().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<CatalogEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<ICatalogReads>();
            entity.HasInMemoryStore(row => row.Id, row => new CatalogEntry
                { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection(row => new CatalogItem { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price });
            var text = entity.HasFragment<CatalogCriteria>("SearchText", (row, criteria) => row.Name.Contains(criteria.Search!));

            entity.HasQuery("Find").UseContextKey<CatalogLookup, CatalogPartition>(request => request.Partition)
                .Where<CatalogLookup>((row, request) => row.Id == request.Id).ProjectTo<CatalogItem>().SingleOrDefault();
            entity.HasQuery("Search").UseContextKey<CatalogCriteria, CatalogPartition>(criteria => criteria.Partition)
                .Validate<CatalogCriteria>(criteria => criteria.MinimumPrice >= 0, "Minimum price cannot be negative.")
                .Where<CatalogCriteria>((row, criteria) => row.Price >= criteria.MinimumPrice)
                .UseIf<CatalogCriteria>(criteria => !string.IsNullOrEmpty(criteria.Search), text)
                .WhereIn<int, CatalogCriteria>(row => row.Id, criteria => criteria.Ids,
                    values => values.NullInput(RepositoryMembershipInputBehavior.IgnoreFilter).Distinct().MaximumCount(20))
                .ProjectTo<CatalogItem>().Results<CatalogItem>()
                .OrderByField<CatalogCriteria>(criteria => criteria.SortBy, criteria => criteria.Descending,
                    fields => fields.Field("name", row => row.Name).Field("price", row => row.Price).UseDefault("name"))
                .ThenBy(row => row.Id).RequirePagingOrder().Paged();
            entity.HasQuery("Totals").UseContextKey<CatalogCriteria, CatalogPartition>(criteria => criteria.Partition)
                .GroupBy(row => row.Category, group => new CatalogTotals { Category = group.Key, Count = group.Count(), Total = group.Sum(row => row.Price) })
                .Results<CatalogTotals>().OrderBy(row => row.Category).List();
            entity.HasQuery("Stream").UseContextKey<CatalogCriteria, CatalogPartition>(criteria => criteria.Partition)
                .OrderBy(row => row.Id).ProjectTo<CatalogItem>().Stream();
        });
    }
}
