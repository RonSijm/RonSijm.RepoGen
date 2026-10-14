using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemorySharedWrites;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedWrites;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySharedWrites;

var services = new ServiceCollection();
services.AddDraftWorkspaceContextRepositoryTestDoubles();
// Deliberate context/partition selection: test registration does not replace an application's global unit of work.
services.AddScoped<IRepositoryUnitOfWork>(provider => provider.GetRequiredService<DraftWorkspaceContextRepositoryFixture>().Data);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<DraftWorkspaceContextRepositoryFixture>();
var read = scope.ServiceProvider.GetRequiredService<IReadDraftRepository>();
var write = scope.ServiceProvider.GetRequiredService<IWriteDraftRepository>();
var journal = scope.ServiceProvider.GetRequiredService<IWriteJournalEntryRepository>();
var work = scope.ServiceProvider.GetRequiredService<IRepositoryUnitOfWork>();
Check(ReferenceEquals(read, write), "CQRS aliases must share the same fixture repository.");
fixture.Data.DraftStore.Seed(new Draft { Id = 1, Title = "Before", Version = 1 });
fixture.Data.DraftStore.Seed(new Draft { Id = 2, Title = "Hidden", Version = 1, Hidden = true });
fixture.Data.CaptureBaseline();

Check((await write.StageRenameAsync(new(1, "After"))).Status == CommandStatus.Staged, "Stage must not save.");
await journal.StageEntryAsync(new(10, "Recorded"));
Check((await read.FindAsync(1))!.Title == "Before" && fixture.Data.PendingCount == 2, "Named reads must use saved rows.");
Check(await work.SaveChangesAsync() == 2, "One save must include both repositories.");
Check((await read.FindAsync(1)) == new DraftCard(1, "After", 2), "Shared save must apply approved assignments/version increment.");
Check(fixture.Data.JournalEntryStore.Find(10)!.Text == "Recorded", "Shared save must include the journal.");
Check((await write.RenameAsync(new(2, "Forbidden"))).Status == CommandStatus.NotFound, "Root visibility must protect writes.");
Check((await write.RenameAsync(new(1, "Stale", 1))).Status == CommandStatus.Conflict, "Stale input must not mutate data.");

await using (await work.BeginTransactionAsync())
{
    await write.RenameAsync(new(1, "Temporary", 2));
    await journal.StageEntryAsync(new(11, "Never saved"));
    Check((await read.FindAsync(1))!.Title == "Temporary", "Saved writes are visible inside the simulated transaction.");
}
Check((await read.FindAsync(1)) == new DraftCard(1, "After", 2) && fixture.Data.PendingCount == 0, "Disposal must roll back saved rows and discard staged writes.");
Check(fixture.Data.JournalEntryStore.Count == 1, "Rollback must restore all participating stores.");

var created = await write.CreateAsync(new(3, "Caller key"));
Check(created.Value == new DraftCard(3, "Caller key", 1), "Saved result must use explicitly supplied keys/values.");
var deleted = await write.DeleteAsync(new(3, "", 1));
Check(deleted.HasValue && deleted.Value!.Title == "Caller key" && await read.FindAsync(3) is null, "Delete must save and return its designed value.");
fixture.Data.RestoreBaseline();
Check((await read.FindAsync(1))!.Title == "Before" && fixture.Data.JournalEntryStore.Count == 0, "Baselines remain independent of write simulation.");
Console.WriteLine("MemorySharedWrites: designed Create/Patch/Delete, version outcomes, shared Stage/Save and checkpoint rollback passed without EF.");

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
