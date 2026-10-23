using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAdditionalReferences;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>One matching target plus two immutable create-only references; no business workflow or entity graph writes.</summary>
public sealed class ReferenceCatalogRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Operations = ["Merge", "Replace", "Stage"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryAdditionalReferences")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryAdditionalReferences")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAdditionalReferences")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<StoredCollection>(entity =>
        {
            entity.BindReadPort<ICollectionMembershipReader>().BindWritePort<ICollectionMembershipWriter>();
            entity.HasInMemoryStore<CollectionSeed, int>(row => row.Id, row => new(row.Id, row.Version))
                .MapInMemoryEntity<CollectionSeed>(row => new StoredCollection { Id = row.Id, Version = row.Version })
                .MapInMemoryWrites<CollectionSeed>((row, saved) => new(row.Id, row.Version));
            entity.HasInMemoryNavigation(row => row.Memberships);
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => new CollectionView(row.Id, row.Version,
                row.Memberships.OrderBy(link => link.ItemId).Select(link => new MembershipView(link.ItemId, link.Caption,
                    link.Contributor == null ? "" : link.Contributor.Name, link.Source == null ? "" : link.Source.Name, link.Version)).ToList())).SingleOrDefault();
            foreach (var operation in Operations)
            {
                var command = entity.HasCommand(operation).Where<ChangeMemberships>((row, request) => row.Id == request.CollectionId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, ChangeMemberships>(row => row.Version, (row, request) => request.ExpectedVersion)
                    .ReconcileLinks<StoredMembership, MembershipInput, int, ChangeMemberships>(row => row.Memberships, request => request.Items,
                        row => row.ItemId, item => item.ItemId, links => links.TargetReference(row => row.Item)
                            .AdditionalReference(row => row.Contributor).AdditionalReference(row => row.Source).MaximumItems(10)
                            .Add(item => new StoredMembership { ItemId = item.ItemId, ContributorRegion = item.ContributorRegion,
                                ContributorCode = item.ContributorCode, SourceId = item.SourceId, Caption = item.Caption, Version = 1 })
                            .Update(setters => setters.SetProperty(row => row.Caption, (row, item) => item.Caption).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(operation == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : RepositoryCollectionRemovalBehavior.Delete))
                    .SimulateCollectionWrites();
                if (operation == "Stage") command.Stage();
                else command.Save().ReturnsSaved(row => new CollectionView(row.Id, row.Version,
                    row.Memberships.OrderBy(link => link.ItemId).Select(link => new MembershipView(link.ItemId, link.Caption,
                        link.Contributor == null ? "" : link.Contributor.Name, link.Source == null ? "" : link.Source.Name, link.Version)).ToList()));
            }
        });
        modelBuilder.Entity<StoredCatalogItem>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<CatalogItemSeed, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<CatalogItemSeed>(row => new StoredCatalogItem { Id = row.Id, Name = row.Name });
        });
        modelBuilder.Entity<StoredContributor>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ContributorSeed, int>(row => row.Id, row => new(row.Id, row.Region, row.Code, row.Name))
                .MapInMemoryEntity<ContributorSeed>(row => new StoredContributor { Id = row.Id, Region = row.Region, Code = row.Code, Name = row.Name });
        });
        modelBuilder.Entity<StoredImportSource>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ImportSourceSeed, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<ImportSourceSeed>(row => new StoredImportSource { Id = row.Id, Name = row.Name });
        });
        modelBuilder.Entity<StoredMembership>(entity =>
        {
            entity.HasInMemoryStore<MembershipSeed, (int, int)>(row => new ValueTuple<int, int>(row.CollectionId, row.ItemId), row => new(row.CollectionId, row.ItemId, row.ContributorRegion, row.ContributorCode, row.SourceId, row.Caption, row.Version))
                .MapInMemoryEntity<MembershipSeed>(row => new StoredMembership { CollectionId = row.CollectionId, ItemId = row.ItemId, ContributorRegion = row.ContributorRegion,
                    ContributorCode = row.ContributorCode, SourceId = row.SourceId, Caption = row.Caption, Version = row.Version })
                .MapInMemoryWrites<MembershipSeed>((row, saved) => new(row.CollectionId, row.ItemId, row.ContributorRegion, row.ContributorCode, row.SourceId, row.Caption, row.Version));
            entity.HasInMemoryNavigation(row => row.Contributor).HasInMemoryNavigation(row => row.Source);
        });
    }
}
