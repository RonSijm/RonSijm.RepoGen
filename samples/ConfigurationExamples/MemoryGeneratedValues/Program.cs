using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryGeneratedValues;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryGeneratedValues;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryGeneratedValues;

var services = new ServiceCollection(); services.AddGeneratedNotebookContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<GeneratedNotebookContextRepositoryFixture>();
var read = scope.ServiceProvider.GetRequiredService<IReadGeneratedNoteRepository>();
var write = scope.ServiceProvider.GetRequiredService<IWriteGeneratedNoteRepository>();
fixture.Data.GeneratedNoteStore.Seed(new() { Id = 7, Text = "seed", TextLength = 4, Origin = "seeded" });
fixture.Data.CaptureBaseline();
var first = await write.CreateAsync(new("first"));
Check(first.Value == new GeneratedNoteCard(8, "first", 5, "designer"), "Insert adapters must return the generated ID/default/computed values.");
await write.StageCreateAsync(new("second"));
Check((await read.ListAsync()).Count == 2, "Reserved inserts are not saved read roots.");
var third = await write.CreateAsync(new("third", "explicit"));
Check(third.AffectedRows == 2 && third.Value!.Id == 10 && third.Value.Origin == "explicit", "Allocation includes pending reservations, supplied defaults survive, and Save is shared.");
await write.StageRenameAsync(new(7, "pending", 4));
Check((await read.ListAsync())[0].TextLength == 4, "Computed update values must not run at Stage.");
Check(await fixture.Data.SaveChangesAsync() == 1 && (await read.ListAsync())[0].TextLength == 7, "Computed values run once on Save.");
Check((await write.StageRenameAsync(new(7, "stale", 4))).Status == CommandStatus.Conflict, "Expected tokens compare original saved values.");
var batch = await write.RenameManyAsync([new(10, "X", 5), new(8, "longer title", 5)]);
Check(batch.Value![0].Id == 10 && batch.Value[0].TextLength == 1 && batch.Value[1].TextLength == 12, "Saved batches return generated values in input order.");
await using (await fixture.Data.BeginTransactionAsync())
{
    Check((await write.CreateAsync(new("undo"))).Value!.Id == 11, "Next allocation must use saved rows.");
    await write.StageRenameAsync(new(7, "changed again", 7)); await fixture.Data.SaveChangesAsync();
}
Check((await read.ListAsync())[0].TextLength == 7 && fixture.Data.GeneratedNoteStore.Find(11) is null, "Rollback restores generated state too.");
Check((await write.CreateAsync(new("replay"))).Value!.Id == 11, "Row-derived allocation is replayable after rollback; this is not a SQL identity sequence promise.");
fixture.Data.RestoreBaseline();
Check((await write.CreateAsync(new("baseline"))).Value!.Id == 8, "Baseline restore also replays row-derived allocation.");
Console.WriteLine("MemoryGeneratedValues: explicit seed/pending-aware IDs, defaults, save-time computed tokens, ordered batches and rollback replay passed without EF.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
