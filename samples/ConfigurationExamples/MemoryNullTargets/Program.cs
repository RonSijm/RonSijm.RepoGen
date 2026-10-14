using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNullTargets;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNullTargets;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddNullTargetNotebookContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<NullTargetNotebookContextRepositoryFixture>();
var reads = scope.ServiceProvider.GetRequiredService<INotebookSummaryReader>();
fixture.Data.StoredNotebookStore.SeedRange([new(1, "first", false), new(2, "second", false), new(3, "hidden", true), new(4, "empty", false)]);
fixture.Data.StoredNotebookLabelStore.SeedRange([new(1, "red", false), new(2, "blue", true)]);
fixture.Data.StoredNotebookLinkStore.SeedRange([new(1, 1, 2), new(2, 1, 1), new(3, 1, null), new(4, 1, null), new(5, 2, 2), new(6, 3, 1)]);
fixture.Data.CaptureBaseline();
var summaries = await reads.SummariesAsync();
if (summaries.Count != 3 || summaries[0].LinkCount != 4 || summaries[0].MissingCount != 2 ||
    !summaries[0].Labels.SequenceEqual(new string?[] { null, null, "red", "blue" }))
    throw new InvalidOperationException("Missing targets must retain their per-link null slots; related visibility is not silently injected.");
var missing = await reads.HasMissingAsync();
if (missing.Count != 1 || missing[0] != 1) throw new InvalidOperationException("The null-safe presence query must match the designed semantics.");
((List<string?>)summaries[0].Labels).Clear();
if ((await reads.SummariesAsync())[0].Labels.Count != 4) throw new InvalidOperationException("Returned results must be detached from fixture state.");
var page = await reads.PageAsync(2, 1);
if (page.TotalCount != 3 || page.Items.Single().Id != 2) throw new InvalidOperationException("Paging must retain root visibility and the same nullable projection.");
await using var stream = reads.StreamAsync().GetAsyncEnumerator();
if (!await stream.MoveNextAsync() || stream.Current.MissingCount != 2) throw new InvalidOperationException("Cold streams must use the designed null-safe projection.");
fixture.Data.StoredNotebookLinkStore.Seed(new(5, 2, null), InMemoryDuplicateKeyBehavior.Replace);
if (!await stream.MoveNextAsync() || stream.Current.MissingCount != 0) throw new InvalidOperationException("An existing enumeration must retain its coordinated snapshot.");
if ((await reads.SummariesAsync())[1].MissingCount != 1) throw new InvalidOperationException("Fresh reads must see the changed nullable relationship.");
fixture.Data.RestoreBaseline();
if ((await reads.SummariesAsync())[1].MissingCount != 0) throw new InvalidOperationException("Baseline reset must restore all participating stores.");
Console.WriteLine("MemoryNullTargets: opt-in missing-target slots, null-safe counts/presence/DTOs, pages, cold streams and detached baselines passed without EF/models.");
