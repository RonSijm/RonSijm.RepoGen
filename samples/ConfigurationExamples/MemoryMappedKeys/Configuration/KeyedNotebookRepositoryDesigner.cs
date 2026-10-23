using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedKeys;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedKeys;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Explicitly bridges one field-backed alternate key and one shadow FK into detached fixture relationships.</summary>
public sealed class KeyedNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryMappedKeys")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryMappedKeys")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedKeys")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        modelBuilder.Entity<KeyedFolder>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IKeyedFolderReader>();
            entity.HasInMemoryStore<KeyedFolderSeed, int>(row => row.Id, row => new(row.Id, row.Code, row.Name))
                .MapInMemoryEntity<KeyedFolderSeed>(row => new KeyedFolder { Id = row.Id, Name = row.Name })
                .MapInMemoryProperty<KeyedFolderSeed, string>(nameof(KeyedFolder.Code), row => row.Code);
            entity.HasInMemoryNavigation(row => row.Memos, useMappedPropertyKeys: true);
            entity.HasQuery("Cards").OrderBy(row => row.Id)
                .Select(row => new KeyedFolderCard(row.Id, row.Name, row.Memos.OrderBy(memo => memo.Id).Select(memo => memo.Title).ToList())).List();
        });
        modelBuilder.Entity<KeyedMemo>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IKeyedMemoReader>();
            entity.HasInMemoryStore<KeyedMemoSeed, int>(row => row.Id, row => new(row.Id, row.FolderCode, row.Title))
                .MapInMemoryEntity<KeyedMemoSeed>(row => new KeyedMemo { Id = row.Id, Title = row.Title })
                .MapInMemoryProperty<KeyedMemoSeed, string?>("FolderCode", row => row.FolderCode);
            entity.HasInMemoryNavigation(row => row.Folder, useMappedPropertyKeys: true);
            entity.HasQuery("Cards").OrderBy(row => row.Id)
                .Select(row => new KeyedMemoCard(row.Id, row.Title, row.Folder == null ? "none" : row.Folder.Name)).List();
        });
    }
}
