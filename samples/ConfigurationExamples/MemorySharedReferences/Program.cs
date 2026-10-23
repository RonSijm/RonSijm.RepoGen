using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySharedReferences;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemorySharedReferences;

await using var provider = new ServiceCollection().AddSharedAuditContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<SharedAuditContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<ISharedAuditReader>();
var writer = scope.ServiceProvider.GetRequiredService<ISharedAuditWriter>();
fixture.Data.AuditFolderStore.SeedRange([new(1, 1), new(2, 1)]);
fixture.Data.AuditResourceStore.SeedRange([new(1, "One"), new(2, "Two"), new(3, "Three")]);
fixture.Data.AuditActorStore.SeedRange([new(1, 1, "Alice"), new(1, 2, "Bob"), new(2, 1, "Other partition")]);
fixture.Data.AuditLinkStore.SeedRange([new(1, 1, 1, 1, "original"), new(2, 1, 2, 1, "other")]);
fixture.Data.CaptureBaseline();
try
{
    await writer.ReplaceAsync(new(1, 1, [new(1, 1, 1, "valid"), new(2, 3, 1, "wrong actor partition")]));
    throw new InvalidOperationException("An Add mapping must not have its shared partition silently overwritten.");
}
catch (ArgumentException) { }
if (fixture.Data.PendingCount != 0 || (await reader.FindAsync(new(1, 1)))!.Version != 1)
    throw new InvalidOperationException("Rejected shared references must preserve the saved graph and version.");

var replaced = await writer.ReplaceAsync(new(1, 1, [new(999, 1, 999, "changed"), new(1, 3, 2, "new")]));
if (replaced.Status != CommandStatus.Applied || replaced.Value!.Links.Count != 2
    || replaced.Value.Links[0].ActorName != "Alice" || replaced.Value.Links[1].ActorName != "Bob")
    throw new InvalidOperationException("Add-only references on matched inputs must be ignored, not validated or reassigned.");

var staged = await writer.StageAsync(new(1, 1, [new(999, 1, 999, "staged", 2), new(1, 2, 1, "added")], 2));
if (staged.Status != CommandStatus.Staged || (await reader.FindAsync(new(1, 1)))!.Links[0].Caption != "changed")
    throw new InvalidOperationException("Stage must retain detached saved reads.");
if (await fixture.Data.SaveChangesAsync() != 4 || (await reader.FindAsync(new(1, 1)))!.Links[0].Caption != "staged")
    throw new InvalidOperationException("Shared Save must persist the parent and approved join changes.");
if (fixture.Data.AuditLinkStore.Find((2, 1, 2))!.Caption != "other"
    || fixture.Data.AuditActorStore.Count != 3 || fixture.Data.AuditResourceStore.Count != 3)
    throw new InvalidOperationException("Other owners and referenced entities must remain unchanged.");
await using (await fixture.Data.BeginTransactionAsync())
    await writer.ReplaceAsync(new(1, 1, [], 3));
if ((await reader.FindAsync(new(1, 1)))!.Links.Count != 2)
    throw new InvalidOperationException("Rollback must restore the shared-reference graph.");
fixture.Data.RestoreBaseline();
if ((await reader.FindAsync(new(1, 1)))!.Version != 1 || fixture.Data.AuditLinkStore.Count != 2)
    throw new InvalidOperationException("Baseline restore must restore the original joins and version.");
Console.WriteLine("MemorySharedReferences: optional shared additional FK columns, Add-only owner checks, immutable references, Stage/Save and rollback passed without EF.");
