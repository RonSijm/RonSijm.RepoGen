using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeFilters;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Native scalar results use pre-mutation model filtering, not a fresh filtered result read.</summary>
public sealed class NativeNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryNativeFilters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryNativeFilters")
            .UseReturningWrites<SqliteReturningWriter>()
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNativeFilters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<StoredNativeNote>(entity =>
        {
            entity.BindReadPort<INativeNoteReader>(); entity.BindWritePort<INativeNoteWriter>();
            entity.HasInMemoryStore<NativeNoteSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Version, row.Deleted, row.Hidden))
                .MapInMemoryEntity<NativeNoteSeed>(row => new StoredNativeNote { Id = row.Id, Title = row.Title, Version = row.Version, Deleted = row.Deleted, Hidden = row.Hidden })
                .MapInMemoryWrites<NativeNoteSeed>((row, saved) => new(row.Id, row.Title, row.Version, row.Deleted, row.Hidden));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.UseInMemoryModelQueryFilter(); // Read permission is separate from every command below.
            entity.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new NativeNoteCard(row.Id, row.Title, row.Version, row.Deleted)).List();
            entity.HasQuery("All").IgnoreQueryFilters().BypassVisibility("Visible").OrderBy(row => row.Id)
                .Select(row => new NativeNoteSeed(row.Id, row.Title, row.Version, row.Deleted, row.Hidden)).List();
            Configure(entity.HasCommand("Update").Where<NativeNoteInput>((row, input) => row.Id == input.Id)
                .ExpectVersion<int, NativeNoteInput>(row => row.Version, (row, input) => input.ExpectedVersion)
                .Update(setters => setters.SetProperty<string, NativeNoteInput>(row => row.Title, (row, input) => input.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version));
            Configure(entity.HasCommand("ArchiveAll").Where(row => row.Id > 0)
                .Update(setters => setters.SetProperty(row => row.Deleted, true).SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version));
            Configure(entity.HasCommand("Consume").Where<NativeNoteInput>((row, input) => row.Id == input.Id)
                .ExpectVersion<int, NativeNoteInput>(row => row.Version, (row, input) => input.ExpectedVersion).Delete());
            entity.HasCommand("StageTitle").Where<NativeNoteInput>((row, input) => row.Id == input.Id)
                .Patch(setters => setters.SetProperty<string, NativeNoteInput>(row => row.Title, (row, input) => input.Title))
                .ExpectVersion<int, NativeNoteInput>(row => row.Version, (row, input) => input.ExpectedVersion).UseInMemoryModelQueryFilter().Stage();
        });
    }

    private static void Configure(RepositoryCommandBuilder<StoredNativeNote> command)
        => command.ReturnsMany(row => new NativeNoteCard(row.Id, row.Title, row.Version, row.Deleted))
            .SimulateImmediateWrites().UseInMemoryModelQueryFilter();
}
