using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedLinks;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedLinks;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Explicit shared-column reference keys, not a tenancy resolver or automatic authorization policy.</summary>
public sealed class ScopedResourcesRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Operations = ["MergeResources", "ReplaceResources", "StageResources"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemorySharedLinks")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemorySharedLinks")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySharedLinks")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<StoredFolder>(entity =>
        {
            entity.BindReadPort<IResourceLinksReader>().BindWritePort<IResourceLinksWriter>();
            entity.HasInMemoryStore<FolderSeed, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id, row.Version))
                .MapInMemoryEntity<FolderSeed>(row => new StoredFolder { PartitionId = row.PartitionId, Id = row.Id, Version = row.Version })
                .MapInMemoryWrites<FolderSeed>((row, saved) => new(row.PartitionId, row.Id, row.Version));
            entity.HasInMemoryNavigation(row => row.Links);
            entity.HasQuery("Find").Where<FolderKey>((row, key) => row.PartitionId == key.PartitionId && row.Id == key.FolderId)
                .Select(row => new FolderView(row.PartitionId, row.Id, row.Version,
                    row.Links.OrderBy(link => link.ResourceId).Select(link => new FolderResourceView(link.ResourceId,
                        link.Resource == null ? "" : link.Resource.Name, link.Caption, link.Version)).ToList())).SingleOrDefault();
            foreach (var operation in Operations)
            {
                var command = entity.HasCommand(operation).Where<ChangeFolderResources>((row, request) => row.PartitionId == request.PartitionId && row.Id == request.FolderId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, ChangeFolderResources>(row => row.Version, (row, request) => request.ExpectedVersion)
                    .ReconcileLinks<StoredFolderResource, ResourceReference, (int, int), ChangeFolderResources>(row => row.Links, request => request.Resources,
                        row => new ValueTuple<int, int>(row.PartitionId, row.ResourceId), item => new ValueTuple<int, int>(item.PartitionId, item.ResourceId),
                        links => links.AllowSharedReferenceColumns().MaximumItems(10)
                            // PartitionId is part of the target reference AND the owner FK: it must agree with the selected folder.
                            .Add(item => new StoredFolderResource { PartitionId = item.PartitionId, ResourceId = item.ResourceId, Caption = item.Caption, Version = 1 })
                            .Update(setters => setters.SetProperty(row => row.Caption, (row, item) => item.Caption).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(operation == "MergeResources" ? RepositoryCollectionRemovalBehavior.Keep : RepositoryCollectionRemovalBehavior.Delete))
                    .SimulateCollectionWrites();
                if (operation == "StageResources") command.Stage();
                else command.Save().ReturnsSaved(row => new FolderView(row.PartitionId, row.Id, row.Version,
                    row.Links.OrderBy(link => link.ResourceId).Select(link => new FolderResourceView(link.ResourceId,
                        link.Resource == null ? "" : link.Resource.Name, link.Caption, link.Version)).ToList()));
            }
        });
        modelBuilder.Entity<StoredResource>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ResourceSeed, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id, row.Name))
                .MapInMemoryEntity<ResourceSeed>(row => new StoredResource { PartitionId = row.PartitionId, Id = row.Id, Name = row.Name });
        });
        modelBuilder.Entity<StoredFolderResource>(entity =>
        {
            entity.HasInMemoryStore<FolderResourceSeed, (int, int, int)>(row => new ValueTuple<int, int, int>(row.PartitionId, row.FolderId, row.ResourceId), row => new(row.PartitionId, row.FolderId, row.ResourceId, row.Caption, row.Version))
                .MapInMemoryEntity<FolderResourceSeed>(row => new StoredFolderResource { PartitionId = row.PartitionId, FolderId = row.FolderId, ResourceId = row.ResourceId, Caption = row.Caption, Version = row.Version })
                .MapInMemoryWrites<FolderResourceSeed>((row, saved) => new(row.PartitionId, row.FolderId, row.ResourceId, row.Caption, row.Version));
            entity.HasInMemoryNavigation(row => row.Resource);
        });
    }
}
