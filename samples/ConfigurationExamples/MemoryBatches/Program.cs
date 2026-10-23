using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryBatches;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryBatches;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryBatches;

var services = new ServiceCollection();
services.AddBatchWorkspaceContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<BatchWorkspaceContextRepositoryFixture>();
var read = scope.ServiceProvider.GetRequiredService<IReadBatchDocumentRepository>();
var write = scope.ServiceProvider.GetRequiredService<IWriteBatchDocumentRepository>();
fixture.Data.BatchDocumentStore.SeedRange([
    new() { Id = 101, Code = "A", OwnerId = 1, Title = "First", Version = 1 },
    new() { Id = 102, Code = "B", OwnerId = 1, Title = "Second", Version = 1 },
    new() { Id = 103, Code = "C", OwnerId = 2, Title = "Other owner", Version = 1 },
    new() { Id = 104, Code = "D", OwnerId = 1, Title = "Archived", Version = 1, Archived = true }
]);
fixture.Data.CaptureBaseline();

var missing = await write.UpdateTitlesAsync(new(1, [new("A", new("Must not change")), new("C", new("Forbidden"))]));
Check(missing.Status == CommandStatus.NotFound && fixture.Data.PendingCount == 0, "One invisible target must reject the whole batch.");
Check((await read.ListOwnedAsync(1))[0].Title == "First", "An earlier target must not be changed.");
var stale = await write.UpdateTitlesAsync(new(1, [new("A", new("Must not change")), new("B", default, 0)]));
Check(stale.Status == CommandStatus.Conflict && fixture.Data.PendingCount == 0, "A stale last item must not partially stage the batch.");
var updated = await write.UpdateTitlesAsync(new(1, [new("B", new(null)), new("A", default)]));
Check(updated.AffectedRows == 2 && updated.Value![0].Code == "B" && updated.Value[0].Title is null, "Saved values retain input order and supplied null.");
Check(updated.Value![1].Title == "First" && updated.Value[1].Version == 2, "Omitted fields remain unchanged while the explicit version setter runs.");

Check((await write.StageTitlesAsync(new(1, [new("A", new("Staged"), 2)]))).Status == CommandStatus.Staged, "Stage must not save.");
Check((await read.ListOwnedAsync(1))[0].Title == "First", "Reads must use saved state.");
Check(await fixture.Data.SaveChangesAsync() == 1, "One explicit shared save commits the staged batch.");
await using (await fixture.Data.BeginTransactionAsync())
{
    var deleted = await write.DeleteDocumentsAsync(new(1, [new("B", default, 2), new("A", default, 3)]));
    Check(deleted.Value![0].Code == "B" && (await read.ListOwnedAsync(1)).Count == 0, "Delete returns pre-deletion rows in input order.");
}
Check((await read.ListOwnedAsync(1)).Count == 2, "Checkpoint rollback restores the whole batch.");
try
{
    await write.UpdateTitlesAsync(new(1, [new("A", default, 3), new("A", default, 3)]));
    throw new InvalidOperationException("Duplicate input should have failed.");
}
catch (ArgumentException) { }
fixture.Data.RestoreBaseline();
Check((await read.ListOwnedAsync(1))[1].Title == "Second", "Baseline restoration is independent of batch transactions.");
Console.WriteLine("MemoryBatches: alternate-key batches, bounded/atomic outcomes, optional fields, shared Stage/Save and rollback passed without EF.");

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
