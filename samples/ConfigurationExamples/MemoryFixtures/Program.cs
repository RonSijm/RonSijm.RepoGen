using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNotebook;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNotebook;
using RonSijm.RepoGen.Testing;

// Arrangement infrastructure is generated; service operations still require explicit scripts in this slice.
var services = new ServiceCollection().AddMemoryNotebookContextRepositoryTestDoubles();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = provider.CreateScope();
var fixture = scope.ServiceProvider.GetRequiredService<MemoryNotebookContextRepositoryFixture>();
var notes = scope.ServiceProvider.GetRequiredService<IMemoryNoteRepositoryTestDouble>();
var labels = scope.ServiceProvider.GetRequiredService<IMemoryLabelRepositoryTestDouble>();
var reader = scope.ServiceProvider.GetRequiredService<IMemoryNoteLookup>();
Ensure(ReferenceEquals(notes.Fixture, labels.Fixture) && ReferenceEquals(fixture, notes.Fixture), "Repositories share one scoped data fixture.");
Ensure(ReferenceEquals(notes, reader), "The application port uses the same generated test double.");

var seed = new MemoryNote { Id = 1, Name = "One", Labels = ["important"] };
fixture.Data.MemoryNoteStore.SeedRange([seed, new() { Id = 2, Name = "Two" }]);
fixture.Data.MemoryLabelStore.Seed(new() { Code = "important", Text = "Important" });
fixture.Data.CaptureBaseline();
seed.Labels.Clear();
fixture.Data.MemoryNoteStore.Find(new(1))!.Labels.Add("inspection edit");
Ensure(fixture.Data.MemoryNoteStore.Snapshot()[0].Labels.SequenceEqual(["important"]), "Seed and inspected values are detached by the declared deep copy.");

try { await reader.FindAsync(1); throw new InvalidOperationException("Seeding must not secretly implement designer queries."); }
catch (UnarrangedRepositoryOperationException) { }
notes.OnFindAsync.Runs((args, _) =>
{
    var row = notes.Fixture.Data.MemoryNoteStore.Find(new(args.Id));
    return Task.FromResult(row is null ? null : new MemoryNoteSummary(row.Id, row.Name));
});
labels.OnCountAsync.Runs((_, _) => Task.FromResult(labels.Fixture.Data.MemoryLabelStore.Count));
Ensure((await reader.FindAsync(1))?.Name == "One" && await reader.FindAsync(99) is null, "Explicit handlers can use typed shared fixture data.");
Ensure(await labels.CountAsync() == 1, "Another repository sees its store in the same fixture.");

fixture.Data.MemoryNoteStore.Seed(new() { Id = 1, Name = "Arranged replacement" }, InMemoryDuplicateKeyBehavior.Replace);
fixture.Data.MemoryLabelStore.Clear();
fixture.Data.RestoreBaseline();
Ensure((await reader.FindAsync(1))?.Name == "One" && await labels.CountAsync() == 1, "Baseline restoration covers both stores and preserves handlers/store instances.");
notes.ResetTestDouble();
Ensure(fixture.Data.MemoryNoteStore.Count == 2, "Script reset and data reset are independent.");
using var otherScope = provider.CreateScope();
Ensure(otherScope.ServiceProvider.GetRequiredService<MemoryNotebookContextRepositoryFixture>().Data.MemoryNoteStore.Count == 0, "Another scope starts isolated and empty.");
Console.WriteLine("MemoryFixtures: scoped sharing, typed keys, deep-copy isolation and cross-store baseline reset passed without EF.");

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
