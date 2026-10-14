using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelWrites;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Independent per-command model filters, saved-row staging, shared save and fresh result visibility.</summary>
public sealed class FilteredDraftsRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] RenameCommands = ["Rename", "StageText"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryModelWrites")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryModelWrites")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryModelWrites")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<StoredFilteredDraft>(entity =>
        {
            entity.BindReadPort<IFilteredDraftReader>().BindWritePort<IFilteredDraftWriter>();
            entity.HasInMemoryStore<FilteredDraftSeed, int>(row => row.Id, row => new(row.Id, row.Text, row.Version, row.Archived, row.Hidden))
                .MapInMemoryEntity<FilteredDraftSeed>(row => new StoredFilteredDraft { Id = row.Id, Text = row.Text, Version = row.Version, Archived = row.Archived, Hidden = row.Hidden })
                .MapInMemoryWrites<FilteredDraftSeed>((row, saved) => new(row.Id, row.Text, row.Version, row.Archived, row.Hidden));
            entity.UseInMemoryModelQueryFilter(); // Reads only; each command opts in separately below.
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new FilteredDraftCard(row.Id, row.Text, row.Version, row.Archived)).List();
            foreach (var name in RenameCommands)
            {
                var command = entity.HasCommand(name).Where<RenameFilteredDraft>((row, request) => row.Id == request.Id)
                    .Patch(setters => setters.SetProperty<string, RenameFilteredDraft>(row => row.Text, (row, request) => request.Text)
                        .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, RenameFilteredDraft>(row => row.Version, (row, request) => request.ExpectedVersion).UseInMemoryModelQueryFilter();
                if (name == "StageText") command.Stage();
                else command.Save().ReturnsSaved(row => new FilteredDraftCard(row.Id, row.Text, row.Version, row.Archived));
            }
            entity.HasCommand("StageArchive").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Archived, true)).UseInMemoryModelQueryFilter().Stage();
            entity.HasCommand("ArchiveAndRead").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Archived, true)).UseInMemoryModelQueryFilter().Save()
                .ReturnsSaved(row => new FilteredDraftCard(row.Id, row.Text, row.Version, row.Archived));
            entity.HasCommand("ExecuteArchive").Where<int>((row, id) => row.Id == id)
                .Update(setters => setters.SetProperty(row => row.Archived, true)).WithOutcome().SimulateImmediateWrites().UseInMemoryModelQueryFilter();
            entity.HasCommand("Create").Create<FilteredDraftSeed>(request => new StoredFilteredDraft
                { Id = request.Id, Text = request.Text, Version = request.Version, Archived = request.Archived, Hidden = request.Hidden })
                .AllowWrite(row => row.Id).AllowWrite(row => row.Version).UseInMemoryModelQueryFilter().Save()
                .Returns(row => new FilteredDraftCard(row.Id, row.Text, row.Version, row.Archived));
        });
        modelBuilder.Entity<StoredFilteredAudit>(entity =>
        {
            entity.ReadEnabled = false; entity.BindWritePort<IFilteredAuditWriter>();
            entity.HasInMemoryStore<FilteredAuditSeed, int>(row => row.Id, row => new(row.Id, row.Message))
                .MapInMemoryEntity<FilteredAuditSeed>(row => new StoredFilteredAudit { Id = row.Id, Message = row.Message })
                .MapInMemoryWrites<FilteredAuditSeed>((row, saved) => new(row.Id, row.Message));
            entity.HasCommand("Stage").Create<FilteredAuditSeed>(request => new StoredFilteredAudit { Id = request.Id, Message = request.Message }).AllowWrite(row => row.Id).Stage();
        });
    }
}
