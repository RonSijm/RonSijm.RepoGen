using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryCollections;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCollections;

var services = new ServiceCollection(); services.AddChecklistWorkspaceContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ChecklistWorkspaceContextRepositoryFixture>();
var parent = scope.ServiceProvider.GetRequiredService<IWriteStoredChecklistRepository>();
var child = scope.ServiceProvider.GetRequiredService<IWriteStoredChecklistItemRepository>();
var read = scope.ServiceProvider.GetRequiredService<IReadStoredChecklistItemRepository>();
fixture.Data.StoredChecklistStore.SeedRange([new(1, 10), new(2, 20)]);
fixture.Data.StoredChecklistItemStore.SeedRange([new(1, 1, "a", "old A"), new(2, 1, "b", "old B"),
    new(3, 1, "hidden", "hidden", Hidden: true), new(4, 2, "other", "other")]);
fixture.Data.CaptureBaseline();
await child.StageTextAsync(4); // Unrelated owner-scoped changes can participate in the shared save.
Check((await parent.StageItemsAsync(new(1, 10, [new("a", "updated"), new("c", "created")]))).Status == CommandStatus.Staged, "Designed staging is explicit.");
Check((await read.ListItemsAsync(1)).Select(row => row.Text).SequenceEqual(["old A", "old B"]), "Queries still see saved child rows.");
Check(await fixture.Data.SaveChangesAsync() == 5, "The parent, child mutations and unrelated staging share one save.");
var created = fixture.Data.StoredChecklistItemStore.Find(5)!;
Check(created.ChecklistId == 1 && created.Code == "c" && created.FixtureNote == "created", "Ownership and seed-aware generated ID are not supplied by the request.");
Check(fixture.Data.StoredChecklistItemStore.Find(1)!.FixtureNote == "arranged", "Explicit write adapters preserve arrangement-only metadata.");
Check(fixture.Data.StoredChecklistItemStore.Find(2) is null && fixture.Data.StoredChecklistItemStore.Find(3)!.Hidden, "Missing visible rows are deleted; hidden rows are retained.");
Check(fixture.Data.StoredChecklistItemStore.Find(4)!.Text == "independent", "Unrelated staging was also saved.");
var stale = await parent.SaveItemsAsync(new(1, 10, [new("a", "stale", 99)], ExpectedVersion: 2));
Check(stale.Status == CommandStatus.Conflict && fixture.Data.PendingCount == 0, "Child conflict restores the whole command, including the parent version.");
await using (await fixture.Data.BeginTransactionAsync())
{
    Check((await parent.SaveItemsAsync(new(1, 10, [], ExpectedVersion: 2))).Value!.Version == 3, "Empty input deliberately deletes visible children.");
    Check((await read.ListItemsAsync(1)).Count == 0, "Saved child deletion is visible inside the fixture transaction.");
}
Check((await read.ListItemsAsync(1)).Count == 2, "Checkpoint rollback restores all participating stores.");
fixture.Data.RestoreBaseline();
Console.WriteLine("MemoryCollections: designed reconciliation, public scalar maps, owned additions, child visibility, versions, shared Stage/Save and rollback passed without EF.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
