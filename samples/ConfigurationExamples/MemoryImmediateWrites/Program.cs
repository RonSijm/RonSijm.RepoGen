using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryImmediateWrites;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryImmediateWrites;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryImmediateWrites;

// Consumer code only: the runner applies DispatchQueueRepositoryDesigner; this app does not reference EF or Models.
var services = new ServiceCollection().AddDispatchQueueContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<DispatchQueueContextRepositoryFixture>();
var reads = scope.ServiceProvider.GetRequiredService<IReadQueuedEnvelopeRepository>();
var writes = scope.ServiceProvider.GetRequiredService<IWriteQueuedEnvelopeRepository>();
var data = fixture.Data;
data.QueuedEnvelopeStore.SeedRange([new(1, "work", "first", 2, 0, 1), new(2, "work", "second", 3, 0, 1),
    new(3, "other", "before", 0, 0, 1), new(4, "work", "hidden", 4, 0, 1, Hidden: true)]);
data.CaptureBaseline();

await writes.StageBodyAsync(new("other", 3, Body: "staged"));
var requeued = await writes.RequeueAsync(new("work"));
Require(requeued.AffectedRows == 2 && requeued.Items.All(row => row.Attempts == row.PreviousAttempts + 1 && row.Version == 2), "setters must read original row values");
Require(data.PendingCount == 1 && data.QueuedEnvelopeStore.Find(3)!.Body == "before", "immediate writes must not flush staged writes");
Require(await data.SaveChangesAsync() == 1 && data.QueuedEnvelopeStore.Find(3)!.Body == "staged", "the explicit shared save remains available");

Require((await writes.ConsumeAsync(new("work", 1, ExpectedVersion: 1))).AffectedRows == 0, "stale version must not consume");
var consumed = await writes.ConsumeAsync(new("work", 1, ExpectedVersion: 2));
Require(consumed.Items.Single().Body == "first" && data.QueuedEnvelopeStore.Find(1) is null, "consume returns the removed scalar row");
Require((await writes.ConsumeAsync(new("work", 1, ExpectedVersion: 2))).AffectedRows == 0, "consuming again returns no rows");

await using (await data.BeginTransactionAsync())
{
    Require((await writes.ClearQueueAsync(new("work"))).AffectedRows == 1, "only visible saved targets are cleared");
    Require((await reads.ListAsync(new("work"))).Count == 0, "immediate writes are visible inside the checkpoint");
} // Uncommitted checkpoint restores all saved stores, including immediate writes.
Require((await reads.ListAsync(new("work"))).Single().Id == 2, "checkpoint must restore immediate deletes");
Require(await writes.MarkAttemptAsync(new("work")) == 1, "count-only updates report directly matched rows");
Require((await writes.ClearQueueAsync(new("missing"))).Status == CommandStatus.NotMatched, "zero-row outcomes do not guess absence versus conflict");
Require(data.QueuedEnvelopeStore.Find(4)!.Hidden, "visibility policies must still apply");
data.RestoreBaseline();
Require(data.QueuedEnvelopeStore.Count == 4 && data.QueuedEnvelopeStore.Find(3)!.Body == "before", "baseline reset restores original saved rows");
Console.WriteLine("MemoryImmediateWrites: opt-in updates, consume, counts/outcomes, staged isolation and rollback passed without EF/model dependencies.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
