using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProjectionHelpers;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryProjectionHelpers;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddProjectionMemosContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ProjectionMemosContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IProjectionMemoReader>();
var writer = scope.ServiceProvider.GetRequiredService<IProjectionMemoWriter>();
fixture.Data.StoredProjectionMemoStore.SeedRange([new(1, " first ", 1, false), new(2, "second", 2, false), new(3, "hidden", 1, true)]);
fixture.Data.CaptureBaseline();
var cards = await reader.CardsAsync();
if (cards.Count != 2 || cards[0].Display != "FIRST" || cards[0].Length != 7) throw new InvalidOperationException("The declared fixture DTO expression must match the original mapper's selected semantics.");
var page = await reader.PageAsync(2, 1);
if (page.TotalCount != 2 || page.Items.Single().Id != 2 || await reader.FindAsync(99) is not null) throw new InvalidOperationException("Mapped DTO pages and missing results must retain their contracts.");
var renamed = await writer.RenameAsync(new(1, " changed ", 1));
if (!renamed.Succeeded || renamed.Value?.Display != "CHANGED" || fixture.Data.StoredProjectionMemoStore.Find(1)?.Text != " changed ")
    throw new InvalidOperationException("Result translation must not rewrite or normalize the persisted scalar assignment.");
if ((await writer.RenameAsync(new(1, "stale", 99))).Status != CommandStatus.Conflict) throw new InvalidOperationException("DTO replacement must not bypass version outcomes.");
await using var stream = reader.StreamAsync().GetAsyncEnumerator();
if (!await stream.MoveNextAsync()) throw new InvalidOperationException("The cold stream must return the first DTO.");
fixture.Data.StoredProjectionMemoStore.Seed(new(2, "after", 2, false), InMemoryDuplicateKeyBehavior.Replace);
if (!await stream.MoveNextAsync() || stream.Current.Display != "SECOND") throw new InvalidOperationException("Existing enumeration must retain its snapshot.");
if ((await reader.FindAsync(2))?.Display != "AFTER") throw new InvalidOperationException("Fresh reads must use a fresh DTO expression view.");
fixture.Data.RestoreBaseline();
if ((await reader.FindAsync(1))?.Display != "FIRST") throw new InvalidOperationException("Baseline restoration must remain isolated from DTO results.");
Console.WriteLine("MemoryProjectionHelpers: explicit flat DTO helper translations, named projection reuse, pages, streams and fresh versioned saved results passed without EF/models.");
