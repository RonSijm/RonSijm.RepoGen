using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryContextFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryContextFilters;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Explicit context-member recipes for fixture filters; no fixture DbContext or inferred tenant service.</summary>
public sealed class TenantNotebookRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] RenameNames = ["Rename", "StageRename"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryContextFilters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryContextFilters")
            .UseKeyedContextLeases<TenantPartition>()
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryContextFilters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());

        // A fixture-only symbol binding. The EF model still owns the filter; neither lambda runs during generation.
        modelBuilder.HasInMemoryModelFilterContext<TenantNotebookContext, TenantPartition, string>(
            context => context.TenantId, partition => partition.TenantId);
        modelBuilder.Entity<TenantNote>(entity =>
        {
            entity.BindReadPort<ITenantNoteReader>(); entity.BindWritePort<ITenantNoteWriter>();
            entity.HasInMemoryStore<TenantNoteSeed, int>(note => note.Id, note => new(note.Id, note.TenantId, note.Title, note.Deleted))
                .MapInMemoryEntity<TenantNoteSeed>(note => new TenantNote { Id = note.Id, TenantId = note.TenantId, Title = note.Title, Deleted = note.Deleted })
                .MapInMemoryWrites<TenantNoteSeed>((note, saved) => new(note.Id, note.TenantId, note.Title, note.Deleted));
            entity.UseInMemoryModelQueryFilter(); // Binding alone does not enable read or write filtering.
            entity.HasQuery("Cards").UseContextKey<TenantNoteRequest, TenantPartition>(input => input.Partition)
                .OrderBy(note => note.Id).Select(note => new TenantNoteCard(note.Id, note.Title)).List();
            entity.HasQuery("Unfiltered").UseContextKey<TenantNoteRequest, TenantPartition>(input => input.Partition)
                .IgnoreQueryFilters().OrderBy(note => note.Id).Select(note => new TenantNoteCard(note.Id, note.Title)).List();
            foreach (var name in RenameNames)
            {
                var command = entity.HasCommand(name).UseContextKey<TenantNoteRequest, TenantPartition>(input => input.Partition)
                    .Where<TenantNoteRequest>((note, input) => note.Id == input.Id)
                    .Patch(setters => setters.SetProperty<string, TenantNoteRequest>(note => note.Title, (note, input) => input.Title))
                    .UseInMemoryModelQueryFilter(); // Each command has its own explicit permission.
                if (name == "StageRename") command.Stage();
                else command.Save().ReturnsSaved(note => new TenantNoteCard(note.Id, note.Title));
            }
            entity.HasCommand("Archive").UseContextKey<TenantNoteRequest, TenantPartition>(input => input.Partition)
                .Where<TenantNoteRequest>((note, input) => note.Id == input.Id).Update(setters => setters.SetProperty(note => note.Deleted, true))
                .WithOutcome().SimulateImmediateWrites().UseInMemoryModelQueryFilter();
        });
    }
}
