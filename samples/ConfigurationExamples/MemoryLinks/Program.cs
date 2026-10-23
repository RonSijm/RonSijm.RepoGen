using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryLinks;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryLinks;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryLinks;

await using var provider = new ServiceCollection().AddBoardLabelsContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<BoardLabelsContextRepositoryFixture>();
var boards = scope.ServiceProvider.GetRequiredService<IReadStoredBoardRepository>();
var writes = scope.ServiceProvider.GetRequiredService<IWriteStoredBoardRepository>();
fixture.Data.StoredBoardStore.SeedRange([new(1, 1), new(2, 1)]);
fixture.Data.StoredLabelStore.SeedRange([new(1, "Shared"), new(2, "Old"), new(3, "New")]);
fixture.Data.StoredBoardLabelStore.SeedRange([new(1, 1, "first", 1), new(1, 2, "old", 1), new(2, 1, "other", 1)]);
fixture.Data.CaptureBaseline();
var merged = await writes.MergeLabelsAsync(new(1, 1, [new(3, "added")]));
if (merged.Status != CommandStatus.Applied || !merged.Value!.Labels.SequenceEqual(["Shared", "Old", "New"]))
    throw new InvalidOperationException("Merge must retain missing links and read fresh saved label names.");
var staged = await writes.StageLabelsAsync(new(1, 2, [new(1, "updated"), new(3, "retained")]));
if (staged.Status != CommandStatus.Staged || (await boards.FindAsync(1))!.Labels.Count != 3)
    throw new InvalidOperationException("Staged join writes are not saved reads.");
await fixture.Data.SaveChangesAsync();
if (!(await boards.FindAsync(1))!.Labels.SequenceEqual(["Shared", "New"]) || fixture.Data.StoredBoardLabelStore.Find((1, 1))!.Caption != "updated")
    throw new InvalidOperationException("Shared save must apply link removals and payload changes.");
if (fixture.Data.StoredBoardLabelStore.Find((2, 1))!.Caption != "other" || fixture.Data.StoredLabelStore.Count != 3)
    throw new InvalidOperationException("Other owners and related label entities must remain untouched.");
await using (await fixture.Data.BeginTransactionAsync())
{
    await writes.ReplaceLabelsAsync(new ChangeLabels(1, 3, [new(2, "rollback")]));
}
if (!(await boards.FindAsync(1))!.Labels.SequenceEqual(["Shared", "New"])) throw new InvalidOperationException("Checkpoint rollback must restore join rows and parent version.");
fixture.Data.RestoreBaseline();
if (!(await boards.FindAsync(1))!.Labels.SequenceEqual(["Shared", "Old"])) throw new InvalidOperationException("Baseline restore must include all three stores.");
Console.WriteLine("MemoryLinks: explicit join creation/payload updates/removal, saved DTOs, Stage/shared Save and rollback passed without EF/model dependencies.");
