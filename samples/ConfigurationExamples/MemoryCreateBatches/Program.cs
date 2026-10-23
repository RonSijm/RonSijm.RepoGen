using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryCreateBatches;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCreateBatches;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCreateBatches;

var services = new ServiceCollection(); services.AddIntakeCatalogContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<IntakeCatalogContextRepositoryFixture>();
var entries = scope.ServiceProvider.GetRequiredService<IWriteIntakeEntryRepository>();
var folders = scope.ServiceProvider.GetRequiredService<IWriteIntakeFolderRepository>();
fixture.Data.IntakeFolderStore.Seed(new(1, "inbox"));
fixture.Data.IntakeEntryStore.Seed(new(7, 1, "baseline")); fixture.Data.CaptureBaseline();
Check((await entries.StageEntriesAsync(new([new(1, " staged ")]))).Status == CommandStatus.Staged, "A batch can stage without saving.");
await folders.StageRenameAsync(1);
Check(fixture.Data.IntakeEntryStore.Count == 1 && fixture.Data.PendingCount == 2, "Reads remain saved-only.");
var result = await entries.CreateEntriesAsync(new([new(1, " second "), new(1, " first ")], "import"));
Check(result.AffectedRows == 4, "Saving includes an earlier batch and another repository's pending write.");
Check(result.Value!.SequenceEqual([new CreatedEntry(9, "second", "renamed", "import"), new CreatedEntry(10, "first", "renamed", "import")]),
    "Generated IDs are reserved and fresh DTOs follow input order, not sort order.");
await entries.StageEntriesAsync(new([new(1, "pending")]));
Check((await entries.CreateEntriesAsync(new([]))).Status == CommandStatus.NoChanges && fixture.Data.PendingCount == 1, "Empty input does not flush pending writes.");
try { await entries.CreateEntriesAsync(new([new(1, "valid"), new(1, null!)])); throw new InvalidOperationException("Expected mapping failure."); }
catch (NullReferenceException) { Check(fixture.Data.PendingCount == 1, "Every mapping is prepared before any new row is staged."); }
Check(await fixture.Data.SaveChangesAsync() == 1, "The earlier stage remains usable after failed preparation.");
await using (await fixture.Data.BeginTransactionAsync())
{
    await entries.CreateEntriesAsync(new([new(1, "temporary")]));
    Check(fixture.Data.IntakeEntryStore.Count == 6, "The checkpoint sees the saved insertion.");
}
Check(fixture.Data.IntakeEntryStore.Count == 5, "Uncommitted checkpoint disposal restores saved rows.");
fixture.Data.RestoreBaseline();
Check(fixture.Data.IntakeEntryStore.Count == 1, "Baseline restore resets all fixture tables.");
Console.WriteLine("MemoryCreateBatches: bounded creation, reserved IDs, fresh ordered DTOs, Stage/shared Save, preparation failure and rollback passed without EF.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
