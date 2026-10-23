using RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyDirectory;
using RonSijm.RepoGen.ConfigurationExamples.Models.ProxyDirectory;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ProxyDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ProxyDirectory")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.ProxyDirectory")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ProxyDirectory")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<DirectoryRecord>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IProxyDirectory>();
            entity.HasInMemoryStore<DirectorySeed, int>(row => row.Key,
                    row => new DirectorySeed(row.Key, row.Label, row.Credits, row.Archived))
                .MapInMemoryEntity<DirectorySeed>(row => new DirectoryRecord
                {
                    Id = row.Key, Name = row.Label, Points = row.Credits * 10, Disabled = row.Archived
                });
            entity.HasVisibilityPolicy("Enabled", row => !row.Disabled);
            entity.HasDefaultSort(row => row.Name);
            entity.HasProjection(row => new DirectoryCard(row.Id, row.Name, row.Points));
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<DirectoryCard>().SingleOrDefault();
            entity.HasQuery("Page").ProjectTo<DirectoryCard>().Paged();
            entity.HasQuery("TotalPoints").Results<DirectoryRecord>().Sum(row => row.Points);
            entity.HasQuery("Stream").ProjectTo<DirectoryCard>().Stream();
            // Deliberately not mapped: no implicit defaults and no attempt to read private model behavior.
            entity.HasQuery("InternalTags").RequireTestOverride("Storage-only tags are not represented by DirectorySeed.")
                .Select(row => row.InternalTag).List();
        });
    }
}
