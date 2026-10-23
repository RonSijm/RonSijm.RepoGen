using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCatalog;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCatalog;

var services = new ServiceCollection().AddMemoryCatalogContextRepositoryTestDoubles();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
using var scope = provider.CreateScope();
var repository = scope.ServiceProvider.GetRequiredService<ICatalogReads>();
var doubleRepository = scope.ServiceProvider.GetRequiredService<CatalogEntryRepositoryTestDouble>();
var fixture = doubleRepository.Fixture;
var first = new CatalogPartition("first");
var second = new CatalogPartition("second");
fixture.For(first).CatalogEntryStore.SeedRange(new CatalogEntry[]
{
    new() { Id = 1, Name = "apple", Category = "fruit", Price = 10 },
    new() { Id = 2, Name = "paper", Category = "supplies", Price = 30 },
    new() { Id = 3, Name = "pear", Category = "fruit", Price = 20 },
    new() { Id = 4, Name = "hidden", Category = "fruit", Price = 100, Hidden = true }
});
fixture.For(second).CatalogEntryStore.Seed(new() { Id = 1, Name = "other partition", Price = 5 });
fixture.For(first).CaptureBaseline();

// Services call named, typed operations. No Where/OrderBy/projection configuration is needed here.
var criteria = new CatalogCriteria(first, Search: "ap", Ids: new List<int> { 1, 2, 2 }, SortBy: "PRICE", Descending: true);
var page = await repository.SearchAsync(criteria, 1, 1);
Ensure(page.TotalCount == 2 && page.Items.Single().Id == 2, "Designer criteria/membership/sort/page must execute.");
Ensure((await repository.SearchAsync(criteria, 2, 1)).Items.Single().Id == 1, "Second page.");
Ensure(await repository.FindAsync(new(first, 4)) is null, "Visibility must apply to unique lookups too.");
Ensure((await repository.FindAsync(new(second, 1)))?.Name == "other partition", "Context keys select fixture partitions automatically.");
var totals = await repository.TotalsAsync(new(first));
Ensure(totals.Single(row => row.Category == "fruit") == new CatalogTotals("fruit", 2, 30), "Grouped aggregates exclude hidden rows.");

try { await repository.SearchAsync(criteria with { SortBy = "Name.Length" }, 1, 1); throw new InvalidOperationException("Unsafe sort accepted."); }
catch (ArgumentException) { }
// One scripted failure/result can override a generated backend; reset restores ordinary execution.
doubleRepository.OnFindAsync.EnqueueResult(new CatalogItem(99, "arranged", "", 0));
Ensure((await repository.FindAsync(new(first, 1)))?.Id == 99, "Explicit scripts take precedence.");
doubleRepository.ResetTestDouble();
Ensure((await repository.FindAsync(new(first, 1)))?.Id == 1, "Reset retains backend and fixture data.");

var stream = repository.StreamAsync(new(first));
fixture.For(first).CatalogEntryStore.Seed(new() { Id = 5, Name = "seed before first MoveNext", Price = 1 });
var streamed = new List<int>();
await foreach (var row in stream) streamed.Add(row.Id);
Ensure(streamed.SequenceEqual(new List<int> { 1, 2, 3, 5 }), "Streams are cold and visibility-filtered.");
fixture.For(first).RestoreBaseline();
Ensure(fixture.For(first).CatalogEntryStore.Count == 4 && fixture.For(second).CatalogEntryStore.Count == 1, "Baseline reset is partition-local.");
Console.WriteLine("MemoryCriteria: automatic criteria, projections, reports, paging, streams, keyed visibility and overrides passed without EF.");

static void Ensure(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
