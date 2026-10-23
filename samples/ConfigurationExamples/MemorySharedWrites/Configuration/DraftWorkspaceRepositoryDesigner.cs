using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedWrites;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class DraftWorkspaceRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemorySharedWrites")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemorySharedWrites")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySharedWrites")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<Draft>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new Draft { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => new DraftCard(row.Id, row.Title, row.Version)).SingleOrDefault();
            entity.HasCommand("Create").Create<DraftRequest>(request => new Draft { Id = request.Id, Title = request.Title, Version = 1 })
                .AllowWrite(row => row.Id).AllowWrite(row => row.Version)
                .Validate<DraftRequest>(request => request.Id > 0, "A positive caller-supplied key is required.").Save()
                .Returns(row => new DraftCard(row.Id, row.Title, row.Version));
            entity.HasCommand("StageRename").Where<DraftRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, DraftRequest>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1))
                .AllowWrite(row => row.Version).ExpectVersion<int, DraftRequest>(row => row.Version, (row, request) => request.ExpectedVersion).Stage();
            entity.HasCommand("Rename").Where<DraftRequest>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, DraftRequest>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1))
                .AllowWrite(row => row.Version).ExpectVersion<int, DraftRequest>(row => row.Version, (row, request) => request.ExpectedVersion).Save()
                .Returns(row => new DraftCard(row.Id, row.Title, row.Version));
            entity.HasCommand("Delete").Where<DraftRequest>((row, request) => row.Id == request.Id).TrackedDelete()
                .ExpectVersion<int, DraftRequest>(row => row.Version, (row, request) => request.ExpectedVersion).Save()
                .Returns(row => new DraftCard(row.Id, row.Title, row.Version));
        });
        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.ReadEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new JournalEntry { Id = row.Id, Text = row.Text });
            entity.HasCommand("StageEntry").Create<DraftRequest>(request => new JournalEntry { Id = request.Id, Text = request.Title }).AllowWrite(row => row.Id).Stage();
        });
    }
}
