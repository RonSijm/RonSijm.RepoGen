using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredCollections;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredCollections;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Configure filtered read graphs, collection mutation and saved-result reads independently.</summary>
public sealed class FilteredChecklistRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] SaveModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryFilteredCollections")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Generated.MemoryFilteredCollections")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryFilteredCollections")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries()
            .UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<FilteredChecklist>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<FilteredChecklistSeed, int>(row => row.Id, row => new(row.Id, row.OwnerId, row.Name, row.Version, row.Archived))
                .MapInMemoryEntity<FilteredChecklistSeed>(row => new FilteredChecklist
                    { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version, Archived = row.Archived })
                .MapInMemoryWrites<FilteredChecklistSeed>((row, saved) => new(row.Id, row.OwnerId, row.Name, row.Version, row.Archived));
            entity.UseInMemoryModelQueryFilter(includeNavigations: true);
            entity.HasInMemoryNavigation(row => row.Items);
            entity.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new FilteredChecklistView(row.Id, row.Name, row.Version,
                row.Items.OrderBy(item => item.Code).Select(item => item.Text).ToList())).List();
            foreach (var save in SaveModes)
            {
                var command = entity.HasCommand(save ? "ReplaceItems" : "StageItems")
                    .Where<FilteredChecklistChange>((row, input) => row.Id == input.Id && row.OwnerId == input.OwnerId)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, FilteredChecklistChange>(row => row.Version, (row, input) => input.ExpectedVersion)
                    .ReconcileCollection<FilteredChecklistItem, FilteredChecklistItemInput, string, FilteredChecklistChange>(row => row.Items, input => input.Items, row => row.Code, item => item.Code,
                        collection => collection.MaximumItems(5)
                            .Add(item => new FilteredChecklistItem { Code = item.Code, Text = item.Text, Version = 1, Archived = item.Archived, Hidden = false })
                            .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text)
                                .SetProperty(row => row.Archived, (row, item) => item.Archived).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
                    .UseInMemoryModelQueryFilter(includeNavigations: save)
                    .SimulateCollectionWrites(includeModelQueryFilters: true);
                if (save) command.Save().ReturnsSaved(row => new FilteredChecklistView(row.Id, row.Name, row.Version,
                    row.Items.OrderBy(item => item.Code).Select(item => item.Text).ToList()));
                else command.Stage();
            }
        });
        modelBuilder.Entity<FilteredChecklistItem>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<FilteredChecklistItemSeed, int>(row => row.Id,
                    row => new(row.Id, row.ChecklistId, row.Code, row.Text, row.Version, row.Archived, row.Hidden))
                .MapInMemoryEntity<FilteredChecklistItemSeed>(row => new FilteredChecklistItem
                    { Id = row.Id, ChecklistId = row.ChecklistId, Code = row.Code, Text = row.Text, Version = row.Version, Archived = row.Archived, Hidden = row.Hidden })
                .MapInMemoryWrites<FilteredChecklistItemSeed>((row, saved) => new(row.Id, row.ChecklistId, row.Code, row.Text, row.Version, row.Archived, row.Hidden));
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, rows) => rows.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
            entity.UseInMemoryModelQueryFilter(includeNavigations: true); // Read/saved-result consent, not write consent.
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasCommand("StageText").Where<int>((row, id) => row.Id == id).BypassVisibility("Visible")
                .Patch(setters => setters.SetProperty(row => row.Text, "pending"))
                .UseInMemoryModelQueryFilter().Stage();
        });
    }
}
