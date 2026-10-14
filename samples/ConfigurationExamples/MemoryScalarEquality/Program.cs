using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScalarEquality;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryScalarEquality;

await using var provider = new ServiceCollection().AddComparisonNotesContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ComparisonNotesContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IComparisonNotesReader>();
var writer = scope.ServiceProvider.GetRequiredService<IComparisonNotesWriter>();
fixture.Data.ComparedNoteStore.SeedRange([new(1, "Alpha"), new(2, null)]);
var equivalent = await writer.SetCaptionAsync(new(1, "ALPHA"));
if (equivalent.AffectedRows != 0 || equivalent.Value?.Caption != "Alpha")
    throw new InvalidOperationException("Declared value equivalence must preserve the original saved representation.");
if ((await reader.MatchingAsync("ALPHA")).Count != 0 || !(await reader.MatchingAsync("Alpha")).SequenceEqual([1]))
    throw new InvalidOperationException("Scalar value equality must not silently rewrite query predicates.");
if ((await writer.StageCaptionAsync(new(1, "alpha"))).Status != CommandStatus.Staged || await fixture.Data.SaveChangesAsync() != 0)
    throw new InvalidOperationException("Stage/shared Save must retain declared value equivalence.");
var changed = await writer.RenameAsync(new(1, "new", "v1", "V2"));
if (changed.Value != new ComparisonNoteSeed(1, "new", "V2") || changed.AffectedRows != 1)
    throw new InvalidOperationException("Explicit token equality must apply without disabling protected-write permissions.");
if ((await writer.RenameAsync(new(1, "stale", "v1"))).Status != CommandStatus.Conflict)
    throw new InvalidOperationException("Equality is not an automatic stale-version retry.");
await using (await fixture.Data.BeginTransactionAsync())
{
    if ((await writer.DeleteAsync(new(1, null, "v2"))).Value != 1) throw new InvalidOperationException("Tracked deletion should use declared token equality.");
}
if ((await reader.FindAsync(1)) != changed.Value || (await writer.SetCaptionAsync(new(2, null))).AffectedRows != 0)
    throw new InvalidOperationException("Checkpoint restoration and explicit nullable equality must retain saved rows.");
Console.WriteLine("MemoryScalarEquality: explicit state/token equality, unchanged query predicates, public mapped rows, Stage/Save, conflicts and rollback passed without EF/models.");
