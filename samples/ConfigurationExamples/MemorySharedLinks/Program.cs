using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedLinks;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySharedLinks;

await using var provider = new ServiceCollection().AddScopedResourcesContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ScopedResourcesContextRepositoryFixture>();
var reads = scope.ServiceProvider.GetRequiredService<IResourceLinksReader>();
var writes = scope.ServiceProvider.GetRequiredService<IResourceLinksWriter>();
fixture.Data.StoredFolderStore.SeedRange([new(1, 1, 1), new(2, 1, 1)]);
fixture.Data.StoredResourceStore.SeedRange([new(1, 1, "First"), new(1, 2, "Second"), new(1, 3, "Third"), new(2, 1, "Other partition")]);
fixture.Data.StoredFolderResourceStore.SeedRange([new(1, 1, 1, "first", 1), new(1, 1, 2, "second", 1), new(2, 1, 1, "other", 1)]);
fixture.Data.CaptureBaseline();
try
{
    await writes.ReplaceResourcesAsync(new(1, 1, 1, [new(1, 1, "valid"), new(2, 1, "cross-partition")]));
    throw new InvalidOperationException("Shared reference columns must match the selected owner.");
}
catch (ArgumentException) { }
if ((await reads.FindAsync(new(1, 1)))!.Version != 1 || fixture.Data.PendingCount != 0)
    throw new InvalidOperationException("Rejected references must not leave staged changes.");
var merged = await writes.MergeResourcesAsync(new(1, 1, 1, [new(1, 3, "added")]));
if (merged.Status != CommandStatus.Applied || merged.Value!.Resources.Count != 3)
    throw new InvalidOperationException("Valid shared references must retain merge and fresh-result semantics.");
var staged = await writes.StageResourcesAsync(new(1, 1, 2, [new(1, 1, "updated"), new(1, 3, "retained")]));
if (staged.Status != CommandStatus.Staged || (await reads.FindAsync(new(1, 1)))!.Resources.Count != 3)
    throw new InvalidOperationException("Stage must not change saved reads.");
await fixture.Data.SaveChangesAsync();
var saved = (await reads.FindAsync(new(1, 1)))!;
if (saved.Resources.Count != 2 || saved.Resources[0].Caption != "updated" || saved.Resources[0].Name != "First")
    throw new InvalidOperationException("Shared save must apply only approved join changes and load the existing resource.");
if (fixture.Data.StoredFolderResourceStore.Find((2, 1, 1))!.Caption != "other" || fixture.Data.StoredResourceStore.Count != 4)
    throw new InvalidOperationException("Other owners and resource entities must remain unchanged.");
await using (await fixture.Data.BeginTransactionAsync())
    await writes.ReplaceResourcesAsync(new(1, 1, 3, [new(1, 2, "rollback")]));
if ((await reads.FindAsync(new(1, 1)))!.Resources.Count != 2) throw new InvalidOperationException("Rollback must restore the shared-column graph.");
fixture.Data.RestoreBaseline();
if ((await reads.FindAsync(new(1, 1)))!.Version != 1) throw new InvalidOperationException("Baseline restore must restore versions and links.");
Console.WriteLine("MemorySharedLinks: explicit shared FK columns, owner validation, saved DTOs, Stage/shared Save and rollback passed without EF/model dependencies.");
