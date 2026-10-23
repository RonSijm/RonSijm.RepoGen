using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatchResults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryBatchResults;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>One bounded batch save, one fresh related-data read, results in caller order.</summary>
public sealed class BatchNotebookRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryBatchResults")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Generated.MemoryBatchResults")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryBatchResults")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<BatchNotebook>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<NotebookRow, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Code, row.Name))
                .MapInMemoryEntity<NotebookRow>(row => new BatchNotebook { TenantId = row.TenantId, Id = row.Id, Code = row.Code, Name = row.Name })
                .MapInMemoryWrites<NotebookRow>((row, saved) => new(row.TenantId, row.Id, row.Code, row.Name));
            entity.HasInMemoryNavigation(row => row.Notes);
            entity.HasCommand("RenameMany").Where(row => row.Name == "draft")
                .PatchMany<RenameNotebook, string, RenameNotebooks>(request => request.Items, row => row.Code, item => item.Code,
                    setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 4)
                .Save().ReturnsSaved<NotebookReceipt, RenameNotebooks>((row, request) => new NotebookReceipt(
                    row.TenantId, row.Id, row.Code, row.Name, request.Label,
                    row.Notes.Where(note => !note.Archived).OrderBy(note => note.Id).Select(note => note.Text).ToList()));
        });
        modelBuilder.Entity<BatchNote>(entity =>
        {
            entity.ReadEnabled = false;
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<NoteRow, int>(row => row.Id, row => new(row.Id, row.TenantId, row.NotebookId, row.Text, row.Archived))
                .MapInMemoryEntity<NoteRow>(row => new BatchNote { Id = row.Id, TenantId = row.TenantId, NotebookId = row.NotebookId, Text = row.Text, Archived = row.Archived })
                .MapInMemoryWrites<NoteRow>((row, saved) => new(row.Id, row.TenantId, row.NotebookId, row.Text, row.Archived));
            entity.HasCommand("StageText").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Text, "shared save")).Stage();
        });
    }
}
