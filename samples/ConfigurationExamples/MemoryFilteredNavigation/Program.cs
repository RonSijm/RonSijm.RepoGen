using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredNavigation;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryFilteredNavigation;

await using var provider = new ServiceCollection().AddFilteredShelfContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<FilteredShelfContextRepositoryFixture>();
var shelves = scope.ServiceProvider.GetRequiredService<IFilteredShelfReader>();
var entries = scope.ServiceProvider.GetRequiredService<IFilteredEntryReader>();
fixture.Data.StoredFilteredShelfStore.SeedRange([new(1), new(2, true), new(3)]);
fixture.Data.StoredFilteredEntryStore.SeedRange([new(1, 1, "one"), new(2, 1, "deleted", Deleted: true), new(3, 1, "hidden", Hidden: true), new(4, 2, "archived shelf"), new(5, null, "unassigned")]);
fixture.Data.CaptureBaseline();
var cards = await shelves.CardsAsync();
if (!cards.Select(row => row.Id).SequenceEqual([1, 3]) || !cards[0].Titles.SequenceEqual(["one", "hidden"]) || cards[0].Count != 2)
    throw new InvalidOperationException("Model child filters precede loading; unrelated root visibility is not injected.");
if ((await shelves.WithTitleAsync("deleted")).Count != 0 || !(await shelves.WithTitleAsync("hidden")).SequenceEqual([1]))
    throw new InvalidOperationException("Collection predicates must see the same filtered related rows.");
var references = await entries.CardsAsync();
if (!references.Select(row => row.Id).SequenceEqual([1, 4, 5]) || !references.Select(row => row.VisibleShelfId).SequenceEqual([1, null, null]))
    throw new InvalidOperationException("Filtered optional targets become null without removing referencing roots.");
if (!(await entries.FromAsync()).SequenceEqual(references) || !(await entries.AllAsync()).Select(row => row.Id).SequenceEqual([1, 2, 4, 5]))
    throw new InvalidOperationException("From roots retain policies; IgnoreQueryFilters bypasses model filters, not visibility.");
var all = await shelves.AllAsync();
if (all.Count != 3 || all[0].Count != 3 || fixture.Data.StoredFilteredEntryStore.Count != 5)
    throw new InvalidOperationException("Ignoring filters restores the complete graph without deleting seeds.");
cards[0].Titles.Clear();
if ((await shelves.CardsAsync())[0].Count != 2) throw new InvalidOperationException("Caller mutation must not change fixture graphs.");
var page = await shelves.PageAsync(2, 1);
if (page.TotalCount != 2 || page.Items.Single().Id != 3) throw new InvalidOperationException("Paging counts filtered roots.");
await using var stream = shelves.StreamAsync().GetAsyncEnumerator();
if (!await stream.MoveNextAsync()) throw new InvalidOperationException("Expected a first card.");
fixture.Data.Clear();
if (!await stream.MoveNextAsync() || stream.Current.Id != 3 || (await shelves.CardsAsync()).Count != 0)
    throw new InvalidOperationException("Streams retain one detached graph snapshot.");
fixture.Data.RestoreBaseline();
if ((await shelves.CardsAsync()).Count != 2) throw new InvalidOperationException("Baseline restores unfiltered seeds.");
Console.WriteLine("MemoryFilteredNavigation: optional model filters, collections, optional references, From, ignore/visibility, paging and detached graph snapshots passed without EF/models.");
