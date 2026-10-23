using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeFilters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNativeFilters;

await using var provider = new ServiceCollection().AddNativeNotebookContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<NativeNotebookContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<INativeNoteReader>();
var writer = scope.ServiceProvider.GetRequiredService<INativeNoteWriter>();
fixture.Data.StoredNativeNoteStore.SeedRange([new(1, "one"), new(2, "deleted", Deleted: true), new(3, "hidden", Hidden: true), new(4, "four")]);
fixture.Data.CaptureBaseline();
if ((await writer.UpdateAsync(new(2, "blocked"))).AffectedRows != 0 || (await writer.UpdateAsync(new(3, "blocked"))).AffectedRows != 0)
    throw new InvalidOperationException("Model filtering and designer visibility independently constrain selection.");
var changed = (await writer.UpdateAsync(new(1, "native"))).Items.Single();
if (changed.Title != "native" || changed.Version != 1) throw new InvalidOperationException("Update-return must expose changed values without SaveChanges.");
await writer.StageTitleAsync(new(1, "pending", 1));
var archived = await writer.ArchiveAllAsync();
if (archived.AffectedRows != 2 || archived.Items.Any(row => !row.Deleted) || (await reader.CardsAsync()).Count != 0)
    throw new InvalidOperationException("Native results survive a filter-changing update; fresh reads do not.");
if (fixture.Data.PendingCount != 1) throw new InvalidOperationException("Immediate writes must retain pending tracked state.");
try { await fixture.Data.SaveChangesAsync(); throw new InvalidOperationException("The changed token must detect a pending conflict."); }
catch (RepositoryWriteConflictException) { }
fixture.Data.DiscardChanges(); fixture.Data.RestoreBaseline();
await using (var transaction = await fixture.Data.BeginTransactionAsync())
{
    var consumed = (await writer.ConsumeAsync(new(1, "", 0))).Items.Single();
    if (consumed.Title != "one" || consumed.Version != 0 || consumed.Deleted || (await reader.AllAsync()).Count != 3)
        throw new InvalidOperationException("Delete-return must expose matched pre-delete values.");
    await transaction.RollbackAsync();
}
if ((await reader.AllAsync()).Count != 4 || (await writer.ConsumeAsync(new(1, "", 99))).AffectedRows != 0)
    throw new InvalidOperationException("Rollback restores seeds and a stale token yields zero native matches.");
Console.WriteLine("MemoryNativeFilters: explicit filtered native results, post-update/pre-delete values, retained pending state, token conflicts and checkpoint rollback passed without EF/models.");
