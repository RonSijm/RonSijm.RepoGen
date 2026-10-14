using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatches;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class BatchWorkspaceRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryBatches")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryBatches")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryBatches")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<BatchDocument>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new BatchDocument
                { Id = row.Id, Code = row.Code, OwnerId = row.OwnerId, Title = row.Title, Version = row.Version, Archived = row.Archived });
            entity.ProtectWrite(row => row.OwnerId);
            entity.HasVisibilityPolicy("Current", row => !row.Archived);
            entity.HasProjection<BatchDocumentCard>(row => new BatchDocumentCard(row.Id, row.Code, row.Title, row.Version));
            entity.HasQuery("ListOwned").Where<int>((row, ownerId) => row.OwnerId == ownerId).OrderBy(row => row.Code)
                .ProjectTo<BatchDocumentCard>().List();
            entity.HasCommand("UpdateTitles").Where<BatchDocumentsRequest>((row, request) => row.OwnerId == request.OwnerId)
                .PatchMany<BatchDocumentChange, string, BatchDocumentsRequest>(request => request.Items, row => row.Code, item => item.Code,
                    setters => setters.SetPropertyWhen(row => row.Title, (row, item) => item.Title.Value, item => item.Title.IsSpecified)
                        .SetProperty(row => row.Version, row => row.Version + 1).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 4)
                .AllowWrite(row => row.Version).Save().Returns(row => new BatchDocumentCard(row.Id, row.Code, row.Title, row.Version));
            entity.HasCommand("StageTitles").Where<BatchDocumentsRequest>((row, request) => row.OwnerId == request.OwnerId)
                .PatchMany<BatchDocumentChange, string, BatchDocumentsRequest>(request => request.Items, row => row.Code, item => item.Code,
                    setters => setters.SetPropertyWhen(row => row.Title, (row, item) => item.Title.Value, item => item.Title.IsSpecified)
                        .SetProperty(row => row.Version, row => row.Version + 1).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 4)
                .AllowWrite(row => row.Version).Stage();
            entity.HasCommand("DeleteDocuments").Where<BatchDocumentsRequest>((row, request) => row.OwnerId == request.OwnerId)
                .DeleteMany<BatchDocumentChange, string, BatchDocumentsRequest>(request => request.Items, row => row.Code, item => item.Code,
                    setters => setters.ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximumItems: 4)
                .Save().Returns(row => new BatchDocumentCard(row.Id, row.Code, row.Title, row.Version));
        });
    }
}
