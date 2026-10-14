using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryOptionalReferences;

await using var provider = new ServiceCollection().AddOptionalAssignmentsContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<OptionalAssignmentsContextRepositoryFixture>();
var reads = scope.ServiceProvider.GetRequiredService<IAssignmentReader>();
var writes = scope.ServiceProvider.GetRequiredService<IAssignmentWriter>();
fixture.Data.StoredWorkspaceStore.SeedRange([new(1, 1), new(2, 1)]);
fixture.Data.StoredSubjectStore.SeedRange([new(1), new(2), new(3), new(4)]);
fixture.Data.StoredOriginStore.SeedRange([new(1, 10, "import"), new(2, 20, "manual")]);
fixture.Data.StoredAssignmentStore.SeedRange([new(1, 1, 10, "import", "present", 1), new(1, 2, null, null, "absent", 1), new(2, 1, 20, "manual", "other", 1)]);
fixture.Data.CaptureBaseline();
var result = await writes.ReplaceAsync(new(1, 1,
    [new(1, null, "ignored Add-only partial", "changed present"), new(2, 20, "manual", "changed absent"), new(3, null, null, "new absent")]));
if (result.Status != CommandStatus.Applied || result.Value!.Items[0].OriginPartition != 10 || result.Value.Items[1].OriginPartition is not null || result.Value.Items[2].OriginCode is not null)
    throw new InvalidOperationException("New references may be absent; matched references must retain their original presence and values.");
var before = fixture.Data.StoredAssignmentStore.Snapshot();
try
{
    await writes.ReplaceAsync(new(1, 2, [new(1, null, null, "must not persist", 2), new(4, 20, null, "partial") ]));
    throw new InvalidOperationException("A partial composite reference must fail.");
}
catch (ArgumentException exception) when (exception.Message.Contains("entirely null or entirely present", StringComparison.Ordinal)) { }
if (!fixture.Data.StoredAssignmentStore.Snapshot().SequenceEqual(before) || (await reads.FindAsync(1))!.Version != 2 || fixture.Data.PendingCount != 0)
    throw new InvalidOperationException("Partial-key failure must leave rows, versions and pending state unchanged.");
var staged = await writes.StageAsync(new(1, 2, [new(4, 20, "manual", "staged present")]));
if (staged.Status != CommandStatus.Staged || (await reads.FindAsync(1))!.Items.Count != 3) throw new InvalidOperationException("Stage must not change saved reads.");
await fixture.Data.SaveChangesAsync();
if ((await reads.FindAsync(1))!.Items.Single().OriginCode != "manual" || fixture.Data.StoredOriginStore.Count != 2 || fixture.Data.StoredAssignmentStore.Find((2, 1))!.Caption != "other")
    throw new InvalidOperationException("Only selected link rows are saved, never origins or another workspace.");
await using (await fixture.Data.BeginTransactionAsync())
    await writes.ReplaceAsync(new(1, 3, [new(3, null, null, "rollback absent")]));
if ((await reads.FindAsync(1))!.Items.Single().SubjectId != 4) throw new InvalidOperationException("Rollback must restore optional references.");
fixture.Data.RestoreBaseline();
if ((await reads.FindAsync(1))!.Version != 1) throw new InvalidOperationException("Baseline restore must restore the entire fixture.");
Console.WriteLine("MemoryOptionalReferences: explicit optional composite keys, absent/present initialization, immutable matches, partial-key rejection and Stage/Save/rollback passed without EF/models.");
