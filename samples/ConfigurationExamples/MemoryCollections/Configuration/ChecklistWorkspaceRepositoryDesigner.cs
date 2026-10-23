// ChecklistWorkspaceContext -> the contract facade and this example's persistence/testing/manifest output.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCollections;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCollections;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ChecklistWorkspaceRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] SaveModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryCollections")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryCollections")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCollections")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<StoredChecklist>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<ChecklistSeed, int>(row => row.Id, row => new(row.Id, row.OwnerId, row.Version))
                .MapInMemoryEntity<ChecklistSeed>(row => new StoredChecklist { Id = row.Id, OwnerId = row.OwnerId, Version = row.Version })
                .MapInMemoryWrites<ChecklistSeed>((row, saved) => new(row.Id, row.OwnerId, row.Version));
            foreach (var save in SaveModes)
            {
                var command = entity.HasCommand(save ? "SaveItems" : "StageItems")
                    .Where<ChecklistChange>((row, request) => row.Id == request.Id && row.OwnerId == request.OwnerId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1))
                    .AllowWrite(row => row.Version).ExpectVersion<int, ChecklistChange>(row => row.Version, (row, request) => request.ExpectedVersion)
                    .ReconcileCollection<StoredChecklistItem, ChecklistItemInput, string, ChecklistChange>(row => row.Items, request => request.Items,
                        row => row.Code, item => item.Code, children => children.MaximumItems(5)
                            .Add(item => new StoredChecklistItem { Code = item.Code, Text = item.Text, Version = 1, Hidden = false })
                            .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
                    .SimulateCollectionWrites(); // Explicit fixture-only lowering of the SAME production rules.
                if (save) command.Save().Returns(row => new ChecklistReceipt(row.Id, row.Version)); else command.Stage();
            }
        });
        modelBuilder.Entity<StoredChecklistItem>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<ChecklistItemSeed, int>(row => row.Id,
                    row => new(row.Id, row.ChecklistId, row.Code, row.Text, row.Version, row.Hidden, row.FixtureNote))
                .MapInMemoryEntity<ChecklistItemSeed>(row => new StoredChecklistItem
                    { Id = row.Id, ChecklistId = row.ChecklistId, Code = row.Code, Text = row.Text, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<ChecklistItemSeed>((row, saved) => new(row.Id, row.ChecklistId, row.Code, row.Text, row.Version, row.Hidden,
                    saved == null ? "created" : saved.FixtureNote));
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasQuery("ListItems").Where<int>((row, checklistId) => row.ChecklistId == checklistId).OrderBy(row => row.Code)
                .Select(row => new ChecklistItemView(row.Id, row.Code, row.Text, row.Version)).List();
            entity.HasCommand("StageText").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Text, "independent")).Stage();
        });
    }
}
