using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedFilters;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Scalar writes, separately opted-in filtered saved DTOs, and an explicit shared-save boundary.</summary>
public sealed class SavedShelfRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemorySavedFilters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemorySavedFilters")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySavedFilters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<StoredSavedEntry>(entity =>
        {
            entity.BindReadPort<ISavedEntryReader>(); entity.BindWritePort<ISavedEntryWriter>();
            entity.HasInMemoryStore<SavedEntrySeed, int>(row => row.Id, row => new(row.Id, row.ShelfId, row.Title, row.Deleted, row.Hidden))
                .MapInMemoryEntity<SavedEntrySeed>(row => new StoredSavedEntry { Id = row.Id, ShelfId = row.ShelfId, Title = row.Title, Deleted = row.Deleted, Hidden = row.Hidden })
                .MapInMemoryWrites<SavedEntrySeed>((row, saved) => new(row.Id, row.ShelfId, row.Title, row.Deleted, row.Hidden));
            entity.UseInMemoryModelQueryFilter(false); // Read permission does not govern these commands' root.
            entity.HasInMemoryNavigation(row => row.Shelf);
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            ConfigureResult(entity.HasCommand("Rename").Where<SavedEntryInput>((row, input) => row.Id == input.Id)
                .Patch(setters => setters.SetProperty<string, SavedEntryInput>(row => row.Title, (row, input) => input.Title)));
            ConfigureResult(entity.HasCommand("RenameMany").PatchMany<SavedEntryInput, int, IReadOnlyList<SavedEntryInput>>(
                inputs => inputs, row => row.Id, input => input.Id, setters => setters.SetProperty(row => row.Title, (row, input) => input.Title), maximumItems: 3));
            ConfigureResult(entity.HasCommand("Create").Create<SavedEntryInput>(input => new StoredSavedEntry
                { Id = input.Id, ShelfId = input.ShelfId, Title = input.Title, Deleted = input.Deleted, Hidden = input.Hidden })
                .AllowWrite(row => row.Id).AllowWrite(row => row.ShelfId));
            ConfigureResult(entity.HasCommand("CreateMany").CreateMany<SavedEntryInput, IReadOnlyList<SavedEntryInput>>(inputs => inputs, input => new StoredSavedEntry
                { Id = input.Id, ShelfId = input.ShelfId, Title = input.Title, Deleted = input.Deleted, Hidden = input.Hidden }, maximumItems: 3)
                .AllowWrite(row => row.Id).AllowWrite(row => row.ShelfId));
            ConfigureResult(entity.HasCommand("Archive").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Deleted, true)));
            entity.HasQuery("All").IgnoreQueryFilters().BypassVisibility("Visible").OrderBy(row => row.Id)
                .Select(row => new SavedEntrySeed(row.Id, row.ShelfId, row.Title, row.Deleted, row.Hidden)).List();
        });
        modelBuilder.Entity<StoredSavedShelf>(entity =>
        {
            entity.ReadEnabled = false; entity.BindWritePort<ISavedShelfWriter>();
            entity.HasInMemoryStore<SavedShelfSeed, int>(row => row.Id, row => new(row.Id, row.Archived))
                .MapInMemoryEntity<SavedShelfSeed>(row => new StoredSavedShelf { Id = row.Id, Archived = row.Archived })
                .MapInMemoryWrites<SavedShelfSeed>((row, saved) => new(row.Id, row.Archived));
            entity.UseInMemoryModelQueryFilter(includeNavigations: true); // Separate permission as a related dataset.
            entity.HasInMemoryNavigation(row => row.Entries);
            entity.HasCommand("StageArchive").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Archived, true))
                .UseInMemoryModelQueryFilter().Stage();
        });
    }

    private static void ConfigureResult(RepositoryCommandBuilder<StoredSavedEntry> command)
        => command.UseInMemoryModelQueryFilter(includeNavigations: true).Save()
            .ReturnsSaved(row => new SavedEntryCard(row.Id, row.Title, row.Shelf == null ? null : row.Shelf.Id,
                row.Shelf == null ? 0 : row.Shelf.Entries.Count));
}
