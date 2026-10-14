// IntakeCatalogContext -> exactly this example's contracts, persistence, testing and manifest outputs.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCreateBatches;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCreateBatches;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class IntakeCatalogRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryCreateBatches")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryCreateBatches")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCreateBatches")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<IntakeEntry>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<EntrySeed, int>(row => row.Id, row => new(row.Id, row.FolderId, row.Title))
                .MapInMemoryEntity<EntrySeed>(row => new IntakeEntry { Id = row.Id, FolderId = row.FolderId, Title = row.Title })
                .MapInMemoryWrites<EntrySeed>((row, saved) => new(row.Id, row.FolderId, row.Title));
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
            entity.HasInMemoryNavigation(row => row.Folder); // Explicit fixture relationship, not graph insertion.
            entity.HasCommand("CreateEntries").CreateMany<NewEntry, CreateEntryBatch>(request => request.Items,
                    item => new IntakeEntry { FolderId = item.FolderId, Title = item.Title.Trim() }, maximumItems: 5)
                .AllowWrite(row => row.FolderId).Save()
                .ReturnsSaved<CreatedEntry, CreateEntryBatch>((row, request) => new CreatedEntry(row.Id, row.Title,
                    row.Folder == null ? "missing" : row.Folder.Name, request.Label));
            entity.HasCommand("StageEntries").CreateMany<NewEntry, CreateEntryBatch>(request => request.Items,
                    item => new IntakeEntry { FolderId = item.FolderId, Title = item.Title.Trim() }, maximumItems: 5)
                .AllowWrite(row => row.FolderId).Stage();
        });
        modelBuilder.Entity<IntakeFolder>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<FolderSeed, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<FolderSeed>(row => new IntakeFolder { Id = row.Id, Name = row.Name })
                .MapInMemoryWrites<FolderSeed>((row, saved) => new(row.Id, row.Name));
            entity.HasCommand("StageRename").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Name, "renamed")).Stage();
        });
    }
}
