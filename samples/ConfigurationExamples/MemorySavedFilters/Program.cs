using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySavedFilters;

await using var provider = new ServiceCollection().AddSavedShelfContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<SavedShelfContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<ISavedEntryReader>();
var writer = scope.ServiceProvider.GetRequiredService<ISavedEntryWriter>();
var shelves = scope.ServiceProvider.GetRequiredService<ISavedShelfWriter>();
fixture.Data.StoredSavedShelfStore.SeedRange([new(1), new(2, true)]);
fixture.Data.StoredSavedEntryStore.SeedRange([new(1, 1, "one"), new(2, 1, "deleted", Deleted: true), new(3, 1, "hidden", Hidden: true), new(4, 2, "archived shelf"), new(5, null, "unassigned")]);
fixture.Data.CaptureBaseline();
var renamed = (await writer.RenameAsync(new(1, "renamed"))).Value ?? throw new InvalidOperationException("Expected a saved result.");
if (renamed.VisibleShelfId != 1 || renamed.RelatedCount != 2) throw new InvalidOperationException("Saved collections filter deleted rows, not unrelated root visibility.");
if ((await writer.RenameAsync(new(2, "blocked"))).Status != CommandStatus.NotFound || (await writer.RenameAsync(new(3, "blocked"))).Status != CommandStatus.NotFound)
    throw new InvalidOperationException("Command model filters and designer visibility remain selection policies.");
var batch = (await writer.RenameManyAsync([new(5, "fifth"), new(1, "first")])).Value ?? throw new InvalidOperationException("Expected ordered saved results.");
if (!batch.Select(row => row.Id).SequenceEqual([5, 1])) throw new InvalidOperationException("Saved batches retain input order.");
var created = (await writer.CreateManyAsync([new(8, "eight", 2), new(7, "seven", 1)])).Value ?? throw new InvalidOperationException("Expected created results.");
if (!created.Select(row => row.Id).SequenceEqual([8, 7]) || created[0].VisibleShelfId is not null || created[1].RelatedCount != 3)
    throw new InvalidOperationException("Fresh created DTOs must use all saved rows and filtered optional targets.");
await shelves.StageArchiveAsync(1);
var shared = (await writer.RenameAsync(new(1, "shared"))).Value ?? throw new InvalidOperationException("Expected a shared-save result.");
if (shared.VisibleShelfId is not null || shared.RelatedCount != 0 || fixture.Data.PendingCount != 0)
    throw new InvalidOperationException("Fresh graphs observe other repositories' saved filters, without treating filters as save constraints.");
await using (var transaction = await fixture.Data.BeginTransactionAsync())
{
    try { await writer.ArchiveAsync(7); throw new InvalidOperationException("A hidden saved root cannot silently return a DTO."); }
    catch (RepositorySavedResultException) { }
    if (!(await reader.AllAsync()).Single(row => row.Id == 7).Deleted) throw new InvalidOperationException("Result failure occurs after saving.");
    await transaction.RollbackAsync();
}
if ((await reader.AllAsync()).Single(row => row.Id == 7).Deleted) throw new InvalidOperationException("Explicit rollback restores saved state.");
fixture.Data.RestoreBaseline();
if ((await reader.AllAsync()).Count != 5 || fixture.Data.StoredSavedShelfStore.Find(1)!.Archived) throw new InvalidOperationException("Baseline restore retains unfiltered seeds.");
Console.WriteLine("MemorySavedFilters: separately opted-in filtered saved DTOs, collection counts, optional targets, ordered batches, shared Save and post-save rollback passed without EF/models.");
