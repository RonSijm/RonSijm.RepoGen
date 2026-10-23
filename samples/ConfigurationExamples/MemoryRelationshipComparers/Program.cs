using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryRelationshipComparers;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryRelationshipComparers;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddComparedDirectoryContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ComparedDirectoryContextRepositoryFixture>();
var folders = scope.ServiceProvider.GetRequiredService<IFolderDirectoryReader>();
var entries = scope.ServiceProvider.GetRequiredService<IFolderEntryReader>();
fixture.Data.StoredFolderStore.SeedRange([new(1, 10, "alpha", "North"), new(2, 20, "alpha", "South"), new(3, 10, "empty", "Empty")]);
fixture.Data.StoredFolderEntryStore.SeedRange([new(1, 10, "ALPHA", "one"), new(2, 10, "Alpha", "two"), new(3, 20, "ALPHA", "three"), new(4, 10, null, "unassigned")]);
fixture.Data.CaptureBaseline();
var summaries = await folders.SummariesAsync();
if (summaries.Count != 3 || summaries[0].Captions.Count != 2 || summaries[1].Captions.Count != 1 || summaries[2].Captions.Count != 0)
    throw new InvalidOperationException("Only Code is compared case-insensitively; Region remains part of the complete relationship key.");
var names = await entries.FolderNamesAsync();
if (names.Count != 4 || names[0] != "North" || names[2] != "South" || names[3] is not null)
    throw new InvalidOperationException("The inverse reference must share the principal policy; a null FK must never join.");
if ((await folders.ByExactCodeAsync("ALPHA")).Count != 0 || (await folders.ByExactCodeAsync("alpha")).Count != 2)
    throw new InvalidOperationException("Relationship comparison must not rewrite query predicates.");
((List<string>)summaries[0].Captions).Clear();
if ((await folders.SummariesAsync())[0].Captions.Count != 2) throw new InvalidOperationException("Results must be detached.");
await using var stream = folders.StreamAsync().GetAsyncEnumerator();
if (!await stream.MoveNextAsync()) throw new InvalidOperationException("The cold stream must contain the first folder.");
fixture.Data.StoredFolderEntryStore.Seed(new(3, 10, "ALPHA", "moved fixture"), InMemoryDuplicateKeyBehavior.Replace);
if (!await stream.MoveNextAsync() || stream.Current.Captions.Count != 1) throw new InvalidOperationException("Enumeration must keep its coordinated snapshot.");
if ((await folders.SummariesAsync())[1].Captions.Count != 0) throw new InvalidOperationException("Fresh reads must see changed fixture relationships.");
fixture.Data.RestoreBaseline();
fixture.Data.StoredFolderStore.Seed(new(99, 10, "ALPHA", "duplicate alternate key"));
try { await folders.SummariesAsync(); throw new InvalidOperationException("Expected relationship uniqueness failure."); }
catch (InvalidOperationException error) when (error.Message.Contains("owner relationship keys are not unique", StringComparison.Ordinal)) { }
fixture.Data.RestoreBaseline();
Console.WriteLine("MemoryRelationshipComparers: compared composite references/collections, unchanged query/store identity, null FKs, detached streams, uniqueness and baselines passed without EF/models.");
