using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNestedProjections;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNestedProjections;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddBriefingsContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<BriefingsContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IBriefingReader>();
var writer = scope.ServiceProvider.GetRequiredService<IBriefingWriter>();
fixture.Data.StoredBriefingStore.SeedRange([new(1, " first briefing ", 1, false), new(2, "second", 2, false), new(3, "hidden", 1, true)]);
fixture.Data.CaptureBaseline();
var cards = await reader.CardsAsync();
if (cards.Count != 2 || cards[0].Heading.Text != "FIRST BRIEFING" || cards[0].Facts[0].Value != 16 || !cards[0].Words.SequenceEqual(["FIRST", "BRIEFING"]))
    throw new InvalidOperationException("The declared nested result recipe must construct headers, facts and materialized words.");
((List<BriefingFact>)cards[0].Facts).Clear();
if ((await reader.FindAsync(1))?.Facts.Count != 2) throw new InvalidOperationException("Returned collections must be fresh data, not retained fixture graphs.");
var page = await reader.PageAsync(2, 1);
if (page.TotalCount != 2 || page.Items.Single().Id != 2 || await reader.FindAsync(99) is not null)
    throw new InvalidOperationException("Nested projections must retain page and missing-result contracts.");
var renamed = await writer.RenameAsync(new(1, " changed ", 1));
if (!renamed.Succeeded || renamed.Value?.Heading.Text != "CHANGED" || fixture.Data.StoredBriefingStore.Find(1)?.Title != " changed ")
    throw new InvalidOperationException("Fresh saved DTO recipes must not rewrite stored assignments.");
if ((await writer.RenameAsync(new(1, "stale", 99))).Status != CommandStatus.Conflict)
    throw new InvalidOperationException("Nested result data must not bypass expected-version semantics.");
await using var stream = reader.StreamAsync().GetAsyncEnumerator();
if (!await stream.MoveNextAsync()) throw new InvalidOperationException("The cold stream must return nested DTO data.");
fixture.Data.StoredBriefingStore.Seed(new(2, "after", 2, false), InMemoryDuplicateKeyBehavior.Replace);
if (!await stream.MoveNextAsync() || stream.Current.Heading.Text != "SECOND")
    throw new InvalidOperationException("An active enumeration must retain its coordinated snapshot.");
if ((await reader.FindAsync(2))?.Heading.Text != "AFTER") throw new InvalidOperationException("Fresh reads must construct a fresh result.");
fixture.Data.RestoreBaseline();
if ((await reader.FindAsync(1))?.Heading.Text != "FIRST BRIEFING") throw new InvalidOperationException("Baseline restore must remain independent of result objects.");
Console.WriteLine("MemoryNestedProjections: explicit nested DTO helpers, materialized collections, named queries, pages, streams and versioned saved results passed without EF/models.");
