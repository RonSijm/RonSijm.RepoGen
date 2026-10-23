using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryFilteredCollections;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryFilteredCollections;

await using var provider = new ServiceCollection().AddFilteredChecklistContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<FilteredChecklistContextRepositoryFixture>();
var writer = scope.ServiceProvider.GetRequiredService<IWriteFilteredChecklistRepository>();
var reader = scope.ServiceProvider.GetRequiredService<IReadFilteredChecklistRepository>();
var items = scope.ServiceProvider.GetRequiredService<IWriteFilteredChecklistItemRepository>();
fixture.Data.FilteredChecklistStore.SeedRange([new(1, 10, "active"), new(2, 20, "archived", Archived: true)]);
fixture.Data.FilteredChecklistItemStore.SeedRange([new(1, 1, "a", "A"), new(2, 1, "b", "B"),
    new(3, 1, "archived", "archived", Archived: true), new(4, 1, "hidden", "hidden", Hidden: true), new(5, 2, "other", "other")]);
fixture.Data.CaptureBaseline();
var before = await reader.CardsAsync();
if (before.Count != 1 || before[0].Items.Count != 3) throw new InvalidOperationException("Read model filters do not inject child root visibility into a DTO collection.");
if ((await writer.StageItemsAsync(new(1, 10, [new("a", "updated"), new("c", "created")]))).Status != CommandStatus.Staged)
    throw new InvalidOperationException("Reconciliation staging must stay explicit.");
if ((await reader.CardsAsync())[0].Version != 1 || fixture.Data.FilteredChecklistItemStore.Find(1)!.Text != "A")
    throw new InvalidOperationException("Staging must not alter saved read snapshots.");
if (await fixture.Data.SaveChangesAsync() != 4) throw new InvalidOperationException("Parent/update/delete/create share one fixture save.");
if (!fixture.Data.FilteredChecklistItemStore.Find(3)!.Archived || !fixture.Data.FilteredChecklistItemStore.Find(4)!.Hidden || fixture.Data.FilteredChecklistItemStore.Find(6)!.ChecklistId != 1)
    throw new InvalidOperationException("Filtered children stay saved; generated IDs account for every saved row and ownership comes from the parent.");
var saved = await writer.ReplaceItemsAsync(new(1, 10, [new("a", "archived update", Archived: true, ExpectedVersion: 2), new("d", "archived insert", Archived: true)], 2));
if (saved.Value is not { Version: 3 } || !saved.Value.Items.SequenceEqual(["hidden"]) || !fixture.Data.FilteredChecklistItemStore.Find(7)!.Archived)
    throw new InvalidOperationException("Updates/inserts can become filtered; only the separate fresh saved projection filters the result.");
if ((await writer.ReplaceItemsAsync(new(2, 20, [], 99))).Status != CommandStatus.NotFound)
    throw new InvalidOperationException("Parent model filtering remains selection, not an expected-version conflict.");
fixture.Data.RestoreBaseline();
await items.StageTextAsync(4);
try { await writer.ReplaceItemsAsync(new(1, 10, [])); throw new InvalidOperationException("Expected dirty hidden-child rejection."); }
catch (InvalidOperationException exception) when (exception.Message.Contains("pending changes", StringComparison.Ordinal)) { }
if (fixture.Data.PendingCount != 1) throw new InvalidOperationException("A failed reconciliation must retain unrelated prior staging.");
fixture.Data.DiscardChanges();
await using (var transaction = await fixture.Data.BeginTransactionAsync())
{
    await writer.ReplaceItemsAsync(new(1, 10, []));
    await transaction.RollbackAsync();
}
if (fixture.Data.FilteredChecklistItemStore.Count != 5) throw new InvalidOperationException("Explicit rollback restores filtered and visible saved rows.");
Console.WriteLine("MemoryFilteredCollections: independent model-filter permissions, scoped reconciliation, Stage/Save, filtered saved DTOs and rollback passed without EF.");
