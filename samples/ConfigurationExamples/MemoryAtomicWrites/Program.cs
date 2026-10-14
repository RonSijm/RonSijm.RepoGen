using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryAtomicWrites;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicWrites;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicWrites;

// Consumer only: the runner applies ReservationDirectoryRepositoryDesigner. No EF/model/designer/provider reference here.
var services = new ServiceCollection().AddReservationDirectoryContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ReservationDirectoryContextRepositoryFixture>();
var reads = scope.ServiceProvider.GetRequiredService<IReadReservationSlotRepository>();
var writes = scope.ServiceProvider.GetRequiredService<IWriteReservationSlotRepository>();
var data = fixture.Data;
data.ReservationSlotStore.SeedRange([new(10, "main", "existing", "before"), new(20, "main", "hidden", "secret", Hidden: true)]);
data.CaptureBaseline();

var inserted = await writes.EnsureAsync(new("main", "new", "first"));
Require(inserted.Status == CommandStatus.Inserted && inserted.HasValue && inserted.Value!.Id == 21, "explicit seed-aware fixture ID generation");
Require(data.ReservationSlotStore.Find(21)!.FixtureNote == "created", "public write map distinguishes insertion from existing metadata");
Require((await writes.EnsureAsync(new("main", "new", "ignored"))).Status == CommandStatus.AlreadyExists, "existing conflicts never replace saved values");
Require((await writes.EnsureAsync(new("main", "hidden"))).Status == CommandStatus.AlreadyExists, "hidden saved rows still participate in unique conflicts");
Require((await writes.PutAsync(new("main", "hidden", "forbidden"))).Status == CommandStatus.NotMatched, "visibility constrains the update branch");

await writes.StageValueAsync(new("main", Value: "staged", Id: 10));
var updated = await writes.PutAsync(new("main", "new", "second"));
Require(updated.Status == CommandStatus.Updated && updated.Value!.Id == 21 && updated.Value.Version == 2, "upsert preserves saved identity and increments the original version");
Require((await writes.PutAsync(new("main", "new", "stale"))).Status == CommandStatus.NotMatched, "stale version never guesses conflict versus absence");
Require(data.PendingCount == 1 && data.ReservationSlotStore.Find(10)!.Value == "before", "immediate writes do not flush earlier staging");
Require(await data.SaveChangesAsync() == 1 && data.ReservationSlotStore.Find(10)!.Value == "staged", "unrelated staging remains saveable");

await using (await data.BeginTransactionAsync())
{
    Require((await writes.PutAsync(new("main", "rollback", "temporary", ExpectedVersion: 999))).Status == CommandStatus.Inserted, "expected versions constrain update only");
    await writes.PutAsync(new("main", "new", "temporary", ExpectedVersion: 2));
} // Checkpoint disposal restores both insertion and update.
Require((await reads.ListAsync(new("main"))).Count == 2 && data.ReservationSlotStore.Find(21)!.Value == "second", "rollback restores saved-state simulation");
Require((await writes.EnsureAsync(new("other", "new", "independent"))).Status == CommandStatus.Inserted, "the complete composite conflict target is respected");
data.RestoreBaseline();
Require(data.ReservationSlotStore.Count == 2 && data.ReservationSlotStore.Find(10)!.Value == "before", "baseline restores data, not an invented SQL sequence");
Console.WriteLine("MemoryAtomicWrites: opt-in insert/upsert outcomes, hidden conflicts, scalar versions, staging and rollback passed without EF/model/provider dependencies.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
