using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemorySavedResults;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedResults;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySavedResults;

var services = new ServiceCollection(); services.AddSavedNotebookContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<SavedNotebookContextRepositoryFixture>();
var notebooks = scope.ServiceProvider.GetRequiredService<IWriteSavedNotebookRepository>();
var notes = scope.ServiceProvider.GetRequiredService<IWriteSavedNoteRepository>();
fixture.Data.SavedNotebookStore.SeedRange([new(1, "draft"), new(2, "other")]);
fixture.Data.SavedNoteStore.SeedRange([new(1, 1, "a", "A", false), new(2, 1, "b", "B", false),
    new(3, 1, "archive", "hidden", true), new(4, 2, "other", "other", false)]);
fixture.Data.CaptureBaseline();

await notes.StageTextAsync(1);
var renamed = await notebooks.RenameAsync(new(1, "renamed", []));
if (renamed.Status != CommandStatus.Applied || renamed.Value!.Notes[0].Text != "shared save")
    throw new InvalidOperationException("The result must include related rows saved by the shared unit of work.");
var saved = await notebooks.SaveNotesAsync(new(1, "published", [new("a", "updated"), new("new", "added")]));
if (saved.Value!.Name != "published" || !saved.Value.Notes.Select(note => note.Text).SequenceEqual(["updated", "added"]))
    throw new InvalidOperationException("Result projection must read the reconciled saved table, including additions/deletions.");
if (fixture.Data.SavedNoteStore.Snapshot().Single(note => note.Id == 3).Text != "hidden")
    throw new InvalidOperationException("Archived rows must remain saved but explicitly excluded from the result.");
var created = await notes.CreateNoteAsync(new(1, "created", "fresh"));
if (created.Value!.Id != 6 || created.Value.NotebookName != "published")
    throw new InvalidOperationException("Generated keys and a saved parent reference must be available after creation.");
await using (await fixture.Data.BeginTransactionAsync())
{
    await notebooks.SaveNotesAsync(new(1, "rollback", []));
}
if (fixture.Data.SavedNotebookStore.Snapshot().Single(row => row.Id == 1).Name != "published")
    throw new InvalidOperationException("A saved-result read must not prevent explicit checkpoint rollback.");
fixture.Data.RestoreBaseline();
if (fixture.Data.SavedNotebookStore.Snapshot().Single(row => row.Id == 1).Name != "draft")
    throw new InvalidOperationException("Baseline reset must restore the public arranged tables.");
Console.WriteLine("MemorySavedResults: fresh nested DTOs after reconciliation/shared save, created references, explicit visibility and rollback passed without EF.");
