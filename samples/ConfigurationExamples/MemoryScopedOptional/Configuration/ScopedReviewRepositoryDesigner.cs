using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScopedOptional;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Keep ownership non-null while optional reference presence uses independent nullable components.</summary>
public sealed class ScopedReviewRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Operations = ["Replace", "Stage"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryScopedOptional")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryScopedOptional")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryScopedOptional")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<ReviewFolder>(entity =>
        {
            entity.BindReadPort<IScopedReviewReader>().BindWritePort<IScopedReviewWriter>();
            entity.HasInMemoryStore<ReviewFolderSeed, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id, row.Version))
                .MapInMemoryEntity<ReviewFolderSeed>(row => new ReviewFolder { PartitionId = row.PartitionId, Id = row.Id, Version = row.Version })
                .MapInMemoryWrites<ReviewFolderSeed>((row, saved) => new(row.PartitionId, row.Id, row.Version));
            entity.HasInMemoryNavigation(row => row.Links);
            entity.HasQuery("Find").Where<ReviewFolderKey>((row, key) => row.PartitionId == key.PartitionId && row.Id == key.Id)
                .Select(row => new ReviewFolderView(row.PartitionId, row.Id, row.Version, row.Links.OrderBy(link => link.ResourceId)
                    .Select(link => new ReviewReferenceView(link.ResourceId, link.Caption, link.ReviewerRegion, link.ReviewerCode,
                        link.Reviewer == null ? null : link.Reviewer.Name, link.Version)).ToList())).SingleOrDefault();
            foreach (var operation in Operations)
            {
                var command = entity.HasCommand(operation).Where<ChangeReviewReferences>((row, input) => row.PartitionId == input.PartitionId && row.Id == input.FolderId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, ChangeReviewReferences>(row => row.Version, (row, input) => input.ExpectedVersion)
                    .ReconcileLinks<ReviewLink, ReviewReferenceInput, int, ChangeReviewReferences>(row => row.Links, input => input.Items,
                        link => link.ResourceId, item => item.ResourceId, links => links.TargetReference(link => link.Resource)
                            .OptionalAdditionalReference(link => link.Reviewer).AllowSharedReferenceColumns().MaximumItems(10)
                            .Add(item => new ReviewLink { PartitionId = item.PartitionId, ResourceId = item.ResourceId, ReviewerRegion = item.ReviewerRegion,
                                ReviewerCode = item.ReviewerCode, Caption = item.Caption, Version = 1 })
                            .Update(setters => setters.SetProperty(link => link.Caption, (link, item) => item.Caption).SetProperty(link => link.Version, link => link.Version + 1))
                            .AllowWrite(link => link.Version).ExpectVersion(link => link.Version, (link, item) => item.ExpectedVersion)
                            .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).SimulateCollectionWrites();
                if (operation == "Stage") command.Stage();
                else command.Save().ReturnsSaved(row => new ReviewFolderView(row.PartitionId, row.Id, row.Version, row.Links.OrderBy(link => link.ResourceId)
                    .Select(link => new ReviewReferenceView(link.ResourceId, link.Caption, link.ReviewerRegion, link.ReviewerCode,
                        link.Reviewer == null ? null : link.Reviewer.Name, link.Version)).ToList()));
            }
        });
        modelBuilder.Entity<ReviewResource>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ReviewResourceSeed, int>(row => row.Id, row => new(row.Id))
                .MapInMemoryEntity<ReviewResourceSeed>(row => new ReviewResource { Id = row.Id });
        });
        modelBuilder.Entity<ReviewPerson>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore<ReviewPersonSeed, int>(row => row.Id, row => new(row.Id, row.PartitionId, row.Region, row.Code, row.Name))
                .MapInMemoryEntity<ReviewPersonSeed>(row => new ReviewPerson { Id = row.Id, PartitionId = row.PartitionId, Region = row.Region, Code = row.Code, Name = row.Name });
        });
        modelBuilder.Entity<ReviewLink>(entity =>
        {
            entity.HasInMemoryStore<ReviewLinkSeed, (int, int, int)>(row => new ValueTuple<int, int, int>(row.PartitionId, row.FolderId, row.ResourceId), row => new(row.PartitionId, row.FolderId, row.ResourceId, row.ReviewerRegion, row.ReviewerCode, row.Caption, row.Version))
                .MapInMemoryEntity<ReviewLinkSeed>(row => new ReviewLink { PartitionId = row.PartitionId, FolderId = row.FolderId, ResourceId = row.ResourceId, ReviewerRegion = row.ReviewerRegion,
                    ReviewerCode = row.ReviewerCode, Caption = row.Caption, Version = row.Version })
                .MapInMemoryWrites<ReviewLinkSeed>((row, saved) => new(row.PartitionId, row.FolderId, row.ResourceId, row.ReviewerRegion, row.ReviewerCode, row.Caption, row.Version));
            entity.HasInMemoryNavigation(row => row.Reviewer);
        });
    }
}
