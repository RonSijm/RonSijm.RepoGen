using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryWriteTranslations;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryWriteTranslations;

await using var provider = new ServiceCollection().AddPatternNotesContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<PatternNotesContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IPatternNoteReader>();
var writer = scope.ServiceProvider.GetRequiredService<IPatternNoteWriter>();
fixture.Data.StoredPatternNoteStore.SeedRange([new(1, "old"), new(2, "hidden", Hidden: true), new(3, "other")]);
var staged = await writer.StageRenameAsync(new(1, "%OL%", " staged "));
if (staged.Status != CommandStatus.Staged || (await reader.FindAsync(1))?.Title != "old")
    throw new InvalidOperationException("A translated staged command must not save early.");
if (await fixture.Data.SaveChangesAsync() != 1 || (await reader.FindAsync(1)) != new PatternNoteCard(1, "STAGED", 2, 6))
    throw new InvalidOperationException("Shared save and reads must use the same mapped public store.");
var saved = await writer.RenameAsync(new(1, "%stage%", " saved ", 2));
if (saved.Value != new PatternNoteCard(1, "SAVED", 3, 5))
    throw new InvalidOperationException("ReturnsSaved must translate the SQL-only length call on the fresh committed row.");
if ((await writer.RenameAsync(new(1, "%", "stale"))).Status != CommandStatus.Conflict ||
    (await writer.RenameAsync(new(2, "%", "hidden"))).Status != CommandStatus.NotFound)
    throw new InvalidOperationException("Fixture translations cannot disable versions or visibility.");
await using (await fixture.Data.BeginTransactionAsync())
{
    if (await writer.DeleteMatchingAsync("%save%") != 1 || await reader.FindAsync(1) != null)
        throw new InvalidOperationException("The independently opted-in immediate delete should affect only matching saved rows.");
}
if ((await reader.FindAsync(1))?.Title != "SAVED" || fixture.Data.PendingCount != 0)
    throw new InvalidOperationException("Checkpoint rollback must restore translated writes without inventing provider transactions.");
Console.WriteLine("MemoryWriteTranslations: per-command provider/helper translations, Stage/Save, versions, saved SQL projections and immediate-delete rollback passed without EF/models.");
