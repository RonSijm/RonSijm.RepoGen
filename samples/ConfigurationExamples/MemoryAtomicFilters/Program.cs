using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicFilters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicFilters;

await using var provider = new ServiceCollection().AddFilteredReservationContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<FilteredReservationContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IFilteredSlotReader>();
var writer = scope.ServiceProvider.GetRequiredService<IFilteredSlotWriter>();
fixture.Data.FilteredReservationStore.SeedRange([new(1, "area", "public", "one"), new(2, "area", "deleted", "two", Deleted: true), new(3, "area", "hidden", "three", Hidden: true)]);
fixture.Data.CaptureBaseline();
if ((await writer.EnsureAsync(new(100, "area", "deleted", "must not insert"))).Status != CommandStatus.AlreadyExists ||
    (await writer.PutAsync(new(100, "area", "deleted", "must not update"))).Status != CommandStatus.NotMatched)
    throw new InvalidOperationException("The unfiltered conflict probe and filtered update must remain distinct.");
var inserted = await writer.EnsureAsync(new(4, "area", "new", "deleted insert", Deleted: true, Hidden: true));
if (inserted.Status != CommandStatus.Inserted || inserted.Value?.Deleted != true || (await reader.ListAsync()).Count != 1)
    throw new InvalidOperationException("Insertion and its returned DTO are not filtered reads.");
var changed = await writer.PutAsync(new(100, "area", "public", "changed"));
if (changed.Value?.Id != 1 || changed.Value.Version != 1) throw new InvalidOperationException("An upsert preserves the existing key, not the insert candidate's key.");
try { await writer.ArchivePutAsync(new(101, "area", "public", "must not survive", 1)); throw new InvalidOperationException("Expected a filtered result-read failure."); }
catch (InvalidOperationException exception) when (exception.Message.Contains("filtered result read", StringComparison.Ordinal)) { }
if (fixture.Data.FilteredReservationStore.Find(1) is not { Deleted: false, Version: 1, Value: "changed" })
    throw new InvalidOperationException("A failed fixture result read must not commit its candidate.");
await writer.StageValueAsync(new(1, "area", "public", "pending", 1));
await writer.PutAsync(new(100, "area", "public", "atomic", 1));
if (fixture.Data.PendingCount != 1) throw new InvalidOperationException("Native-style operations must retain staged work.");
try { await fixture.Data.SaveChangesAsync(); throw new InvalidOperationException("Expected the staged version to conflict."); }
catch (RepositoryWriteConflictException) { }
fixture.Data.DiscardChanges(); fixture.Data.RestoreBaseline();
await using (var transaction = await fixture.Data.BeginTransactionAsync())
{
    await writer.PutAsync(new(100, "area", "public", "rolled back"));
    await transaction.RollbackAsync();
}
if (fixture.Data.FilteredReservationStore.Find(1)?.Value != "one") throw new InvalidOperationException("Checkpoint rollback must restore saved rows.");
Console.WriteLine("MemoryAtomicFilters: unfiltered conflicts/inserts, filtered updates/result reads, retained keys/staging, token conflicts and rollback passed without EF/models/providers.");
