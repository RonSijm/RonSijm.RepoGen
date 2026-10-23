using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryOptionalLinks;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryOptionalLinks;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddLinkedNoteContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<LinkedNoteContextRepositoryFixture>();
var notes = scope.ServiceProvider.GetRequiredService<IReadLinkedNoteRepository>();
var labels = scope.ServiceProvider.GetRequiredService<IReadNoteLabelRepository>();
fixture.Data.LinkedNoteStore.SeedRange([new(1, 1, "North"), new(1, 2, "Empty"), new(2, 1, "South")]);
fixture.Data.NoteLabelStore.SeedRange([new(1, "Shared", false), new(2, "Hidden", true), new(3, "Unused", false)]);
fixture.Data.NoteLabelLinkStore.SeedRange([new(1, 1, 1, 1), new(2, 1, 1, 2), new(3, 1, 1, null), new(4, 1, 1, null), new(5, 2, 1, 1)]);
fixture.Data.CaptureBaseline();

var cards = await notes.CardsAsync();
if (!cards[0].Labels.SequenceEqual(["Shared", "Hidden"]) || cards[1].Labels.Count != 0 || !cards[2].Labels.SequenceEqual(["Shared"]))
    throw new InvalidOperationException("Nullable links must follow the designed non-null filter and complete composite source keys.");
if (!(await notes.CountsAsync()).SequenceEqual([2, 0, 1]) || !(await notes.PublicCardsAsync())[0].Labels.SequenceEqual(["Shared"]))
    throw new InvalidOperationException("Counts and related visibility must use the same shared designer semantics.");
var usage = await labels.UsageAsync();
if (usage.Count != 2 || usage[0].NoteCount != 2 || usage[1].NoteCount != 0)
    throw new InvalidOperationException("The inverse has a nullable source, not target; only the label-root visibility policy applies.");
try { await notes.RawCountsAsync(); throw new InvalidOperationException("Unguarded SQL null propagation was silently simulated."); }
catch (UnsupportedInMemoryRepositoryOperationException failure)
{
    if (!failure.Reason.Contains("optional target", StringComparison.Ordinal)) throw;
}
var testDouble = scope.ServiceProvider.GetRequiredService<LinkedNoteRepositoryTestDouble>();
testDouble.OnRawCountsAsync.Returns([4, 0, 1]);
if (!(await notes.RawCountsAsync()).SequenceEqual([4, 0, 1])) throw new InvalidOperationException("Explicit scripts must remain available for provider-bound behavior.");
cards[0].Labels.Clear();
fixture.Data.NoteLabelLinkStore.Seed(new(1, 1, 1, null), InMemoryDuplicateKeyBehavior.Replace);
if (!(await notes.CardsAsync())[0].Labels.SequenceEqual(["Hidden"])) throw new InvalidOperationException("Reads must capture fresh detached link rows.");
fixture.Data.RestoreBaseline();
if ((await notes.CardsAsync())[0].Labels.Count != 2) throw new InvalidOperationException("Baseline restore must include nullable links.");
Console.WriteLine("MemoryOptionalLinks: guarded nullable targets, inverse counts, explicit overrides and detached snapshots passed without EF.");
