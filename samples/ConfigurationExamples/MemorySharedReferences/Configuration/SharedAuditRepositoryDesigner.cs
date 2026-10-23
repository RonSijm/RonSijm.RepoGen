using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedReferences;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Share an owner partition with a create-only reference, not with the global matching target.</summary>
public sealed class SharedAuditRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Operations = ["Replace", "Stage"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemorySharedReferences")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemorySharedReferences")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySharedReferences")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<AuditFolder>(entity =>
        {
            entity.BindReadPort<ISharedAuditReader>().BindWritePort<ISharedAuditWriter>();
            entity.HasInMemoryStore<AuditFolderSeed, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id, row.Version))
                .MapInMemoryEntity<AuditFolderSeed>(row => new AuditFolder { PartitionId = row.PartitionId, Id = row.Id, Version = row.Version })
                .MapInMemoryWrites<AuditFolderSeed>((row, saved) => new(row.PartitionId, row.Id, row.Version));
            entity.HasInMemoryNavigation(row => row.Links);
            entity.HasQuery("Find").Where<AuditFolderKey>((row, key) => row.PartitionId == key.PartitionId && row.Id == key.Id)
                .Select(row => new AuditFolderView(row.PartitionId, row.Id, row.Version, row.Links.OrderBy(link => link.ResourceId)
                    .Select(link => new AuditReferenceView(link.ResourceId, link.Caption, link.Actor == null ? "" : link.Actor.Name)).ToList())).SingleOrDefault();
            foreach (var operation in Operations)
            {
                var command = entity.HasCommand(operation).Where<ChangeAuditReferences>((row, input) => row.PartitionId == input.PartitionId && row.Id == input.FolderId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, ChangeAuditReferences>(row => row.Version, (row, input) => input.ExpectedVersion)
                    .ReconcileLinks<AuditLink, AuditReferenceInput, int, ChangeAuditReferences>(row => row.Links, input => input.Items,
                        link => link.ResourceId, item => item.ResourceId, links => links.TargetReference(link => link.Resource)
                            .AdditionalReference(link => link.Actor).AllowSharedReferenceColumns().MaximumItems(10)
                            .Add(item => new AuditLink { PartitionId = item.PartitionId, ResourceId = item.ResourceId, ActorId = item.ActorId, Caption = item.Caption, Version = 1 })
                            .Update(setters => setters.SetProperty(link => link.Caption, (link, item) => item.Caption).SetProperty(link => link.Version, link => link.Version + 1))
                            .AllowWrite(link => link.Version).ExpectVersion(link => link.Version, (link, item) => item.ExpectedVersion)
                            .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).SimulateCollectionWrites();
                if (operation == "Stage") command.Stage();
                else command.Save().ReturnsSaved(row => new AuditFolderView(row.PartitionId, row.Id, row.Version, row.Links.OrderBy(link => link.ResourceId)
                    .Select(link => new AuditReferenceView(link.ResourceId, link.Caption, link.Actor == null ? "" : link.Actor.Name)).ToList()));
            }
        });
        modelBuilder.Entity<AuditResource>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<AuditResourceSeed, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<AuditResourceSeed>(row => new AuditResource { Id = row.Id, Name = row.Name });
        });
        modelBuilder.Entity<AuditActor>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<AuditActorSeed, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id, row.Name))
                .MapInMemoryEntity<AuditActorSeed>(row => new AuditActor { PartitionId = row.PartitionId, Id = row.Id, Name = row.Name });
        });
        modelBuilder.Entity<AuditLink>(entity =>
        {
            entity.HasInMemoryStore<AuditLinkSeed, (int, int, int)>(row => new ValueTuple<int, int, int>(row.PartitionId, row.FolderId, row.ResourceId), row => new(row.PartitionId, row.FolderId, row.ResourceId, row.ActorId, row.Caption, row.Version))
                .MapInMemoryEntity<AuditLinkSeed>(row => new AuditLink { PartitionId = row.PartitionId, FolderId = row.FolderId, ResourceId = row.ResourceId, ActorId = row.ActorId, Caption = row.Caption, Version = row.Version })
                .MapInMemoryWrites<AuditLinkSeed>((row, saved) => new(row.PartitionId, row.FolderId, row.ResourceId, row.ActorId, row.Caption, row.Version));
            entity.HasInMemoryNavigation(row => row.Actor);
        });
    }
}
