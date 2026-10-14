using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryRelationshipComparers;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryRelationshipComparers;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>One explicitly compared alternate-key component, shared by both detached relationship directions.</summary>
public sealed class ComparedDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryRelationshipComparers")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryRelationshipComparers")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryRelationshipComparers")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<StoredFolder>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IFolderDirectoryReader>();
            entity.HasInMemoryStore<FolderSeed, int>(row => row.Id, row => new(row.Id, row.Region, row.Code, row.Name))
                .MapInMemoryEntity<FolderSeed>(row => new StoredFolder { Id = row.Id, Region = row.Region, Code = row.Code, Name = row.Name });
            entity.HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.OrdinalIgnoreCase);
            entity.HasInMemoryNavigation(row => row.Entries);
            entity.HasProjection<FolderSummary>(row => new FolderSummary(row.Name,
                row.Entries.OrderBy(entry => entry.Id).Select(entry => entry.Caption).ToList()));
            entity.HasQuery("Summaries").OrderBy(row => row.Id).ProjectTo<FolderSummary>().List();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<FolderSummary>().Stream();
            // A relationship policy is not a query-rewriting policy: this CLR predicate is still ordinal.
            entity.HasQuery("ByExactCode").Where<string>((row, code) => row.Code == code).OrderBy(row => row.Id).Select(row => row.Id).List();
        });
        modelBuilder.Entity<StoredFolderEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IFolderEntryReader>();
            entity.HasInMemoryStore<FolderEntrySeed, int>(row => row.Id, row => new(row.Id, row.Region, row.FolderCode, row.Caption))
                .MapInMemoryEntity<FolderEntrySeed>(row => new StoredFolderEntry { Id = row.Id, Region = row.Region, FolderCode = row.FolderCode, Caption = row.Caption });
            entity.HasInMemoryNavigation(row => row.Folder);
            entity.HasQuery("FolderNames").OrderBy(row => row.Id).Select(row => row.Folder == null ? null : row.Folder.Name)
                .ResultNullability<string>(type => type.Nullable()).List();
        });
    }
}
