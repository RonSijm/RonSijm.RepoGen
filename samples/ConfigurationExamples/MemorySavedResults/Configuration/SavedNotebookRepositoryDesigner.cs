// SavedNotebookContext -> exact CQRS, persistence, test-double and manifest files listed in README.md.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedResults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedResults;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class SavedNotebookRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] ReconciliationModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemorySavedResults")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemorySavedResults")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySavedResults")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<SavedNotebook>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<NotebookRow, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<NotebookRow>(row => new SavedNotebook { Id = row.Id, Name = row.Name })
                .MapInMemoryWrites<NotebookRow>((row, saved) => new(row.Id, row.Name));
            entity.HasInMemoryNavigation(row => row.Notes); // Separate saved table, not a seed graph.
            foreach (var reconcile in ReconciliationModes)
            {
                var command = entity.HasCommand(reconcile ? "SaveNotes" : "Rename")
                    .Where<SaveNotebook>((row, request) => row.Id == request.Id)
                    .Patch(setters => setters.SetProperty<string, SaveNotebook>(row => row.Name, (row, request) => request.Name));
                if (reconcile) command.ReconcileCollection<SavedNote, NoteInput, string, SaveNotebook>(
                    row => row.Notes, request => request.Notes, row => row.Code, item => item.Code, children => children.MaximumItems(5)
                        .Add(item => new SavedNote { Code = item.Code, Text = item.Text, Archived = false })
                        .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text))
                        .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).SimulateCollectionWrites();
                command.Save().ReturnsSaved(row => new NotebookReceipt(row.Id, row.Name,
                    row.Notes.Where(note => !note.Archived).OrderBy(note => note.Code)
                        .Select(note => new NoteReceipt(note.Id, note.Code, note.Text)).ToList()));
            }
        });
        modelBuilder.Entity<SavedNote>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<NoteRow, int>(row => row.Id, row => new(row.Id, row.NotebookId, row.Code, row.Text, row.Archived))
                .MapInMemoryEntity<NoteRow>(row => new SavedNote { Id = row.Id, NotebookId = row.NotebookId, Code = row.Code, Text = row.Text, Archived = row.Archived })
                .MapInMemoryWrites<NoteRow>((row, saved) => new(row.Id, row.NotebookId, row.Code, row.Text, row.Archived));
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(note => note.Id).DefaultIfEmpty().Max() + 1));
            entity.HasInMemoryNavigation(row => row.Notebook);
            entity.HasVisibilityPolicy("Current", row => !row.Archived);
            entity.HasCommand("CreateNote").Create<NewNote>(request => new SavedNote
                { NotebookId = request.NotebookId, Code = request.Code, Text = request.Text, Archived = false })
                .AllowWrite(row => row.NotebookId).Save().ReturnsSaved(row => new CreatedNoteReceipt(row.Id,
                    row.Notebook == null ? "missing" : row.Notebook.Name, row.Text));
            entity.HasCommand("StageText").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Text, "shared save")).Stage();
        });
    }
}
