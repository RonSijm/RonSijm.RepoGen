using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryProxyWrites;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryProxyWrites;

var services = new ServiceCollection(); services.AddProxyNotebookContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ProxyNotebookContextRepositoryFixture>();
var read = scope.ServiceProvider.GetRequiredService<IReadPrivateNoteRepository>();
var write = scope.ServiceProvider.GetRequiredService<IWritePrivateNoteRepository>();
var audit = scope.ServiceProvider.GetRequiredService<IWritePrivateAuditRepository>();
fixture.Data.PrivateNoteStore.SeedRange([new(7, "old", 3, false, [1, 2]), new(8, "hidden", 6, true, [])]);
fixture.Data.CaptureBaseline();
Check((await read.ListAsync()).Count == 1, "Designed visibility filters public storage rows too.");
await write.StageCreateAsync(new(0, "first")); await write.StageCreateAsync(new(0, "second"));
var marker = new byte[] { 4, 5 };
await write.StageRenameAsync(new(7, "saved", Marker: marker)); marker[0] = 99;
await audit.StageAuditAsync(new(1, "audit"));
Check(fixture.Data.PendingCount == 4 && fixture.Data.PrivateNoteStore.Count == 2, "Stage does not change public saved rows.");
Check(await fixture.Data.SaveChangesAsync() == 4, "Both repositories share one explicitly selected fixture unit of work.");
Check(fixture.Data.PrivateNoteStore.Find(9)!.Caption == "first" && fixture.Data.PrivateNoteStore.Find(10)!.Revision == 6, "Reserved keys and computed values are mapped back on Save.");
var saved = fixture.Data.PrivateNoteStore.Find(7)!;
Check(saved.Revision == 5 && saved.Marker[0] == 4 && saved.Tag == "arranged", "The adapter preserves binary values and arrangement-only metadata.");
Check(fixture.Data.PrivateAuditStore.Find(1)!.Caption == "audit", "The second internal entity is saved without exposing its model.");
Check((await write.RenameAsync(new(7, "stale", 3))).Status == CommandStatus.Conflict, "Expected versions compare saved mapped scalar slots.");
var batch = await write.RenameManyAsync([new(10, "A", 6), new(9, "BC", 5)]);
Check(batch.Value![0].Id == 10 && batch.Value[0].Version == 1 && batch.Value[1].Version == 2, "Batch results keep input order and saved generated values.");
await using (await fixture.Data.BeginTransactionAsync())
{
    await write.RemoveAsync(new(7, "", 5));
    Check(fixture.Data.PrivateNoteStore.Find(7) is null, "Saved deletion updates the public arrangement store.");
}
Check(fixture.Data.PrivateNoteStore.Find(7)!.Caption == "saved", "Checkpoint rollback restores mapped stores too.");
fixture.Data.RestoreBaseline();
Check((await write.CreateAsync(new(0, "replay"))).Value!.Id == 9 && fixture.Data.PrivateAuditStore.Count == 0, "Baseline restore replays row-derived IDs and resets both stores.");
Console.WriteLine("MemoryProxyWrites: public immutable rows, internal models, explicit write adapters, Stage/Save, generated values, batches and rollback passed without EF.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
