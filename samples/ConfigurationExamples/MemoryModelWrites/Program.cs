using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelWrites;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryModelWrites;

await using var provider = new ServiceCollection().AddFilteredDraftsContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<FilteredDraftsContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IFilteredDraftReader>();
var writer = scope.ServiceProvider.GetRequiredService<IFilteredDraftWriter>();
var audit = scope.ServiceProvider.GetRequiredService<IFilteredAuditWriter>();
fixture.Data.StoredFilteredDraftStore.SeedRange([new(1, "one", 0, false, false), new(2, "archived", 0, true, false),
    new(3, "hidden", 0, false, true), new(4, "four", 0, false, false)]);
fixture.Data.CaptureBaseline();
if ((await writer.RenameAsync(new(2, "blocked", 0))).Status != CommandStatus.NotFound) throw new InvalidOperationException("The model filter must protect command selection.");
if ((await writer.RenameAsync(new(1, "stale", 99))).Status != CommandStatus.Conflict) throw new InvalidOperationException("Original version outcomes remain independent of filtering.");
await writer.StageArchiveAsync(1);
if ((await writer.StageTextAsync(new(1, "staged", 0))).Status != CommandStatus.Staged) throw new InvalidOperationException("Selection must use saved rows even after a staged archive.");
await audit.StageAsync(new(50, "shared"));
if (fixture.Data.PendingCount != 2 || await fixture.Data.SaveChangesAsync() != 2) throw new InvalidOperationException("A filtered command must not turn shared Save into a query-filter constraint.");
if (fixture.Data.StoredFilteredDraftStore.Find(1)?.Text != "staged" || fixture.Data.StoredFilteredAuditStore.Find(50)?.Message != "shared")
    throw new InvalidOperationException("Shared save must publish both designed writes.");
if (!(await reader.CardsAsync()).Select(row => row.Id).SequenceEqual([4])) throw new InvalidOperationException("Fresh reads must filter saved archived rows.");
if ((await writer.ExecuteArchiveAsync(4)).AffectedRows != 1 || (await writer.RenameAsync(new(4, "blocked", 0))).Status != CommandStatus.NotFound)
    throw new InvalidOperationException("Immediate counts use saved filtered rows without a second Save.");
var created = await writer.CreateAsync(new(10, "archived insert", 0, true, false));
if (!created.Succeeded || created.Value?.Archived != true) throw new InvalidOperationException("Query filters must not become insertion validation.");
fixture.Data.RestoreBaseline();
await using (var transaction = await fixture.Data.BeginTransactionAsync())
{
    var failedResult = false;
    try { await writer.ArchiveAndReadAsync(1); }
    catch (RepositorySavedResultException) { failedResult = true; }
    if (!failedResult || fixture.Data.StoredFilteredDraftStore.Find(1)?.Archived != true) throw new InvalidOperationException("An invisible fresh result must fail after saving, not silently undo the write.");
    await transaction.RollbackAsync();
}
if (fixture.Data.StoredFilteredDraftStore.Find(1)?.Archived != false || (await reader.CardsAsync()).Count != 2) throw new InvalidOperationException("Explicit checkpoint rollback must restore the saved rows.");
Console.WriteLine("MemoryModelWrites: per-command filters, version outcomes, saved-row staging, cross-repository Save, immediate counts, unfiltered creation and explicit saved-result rollback passed without EF/models.");
