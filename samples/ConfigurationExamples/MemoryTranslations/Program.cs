using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryTranslations;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryTranslations;

await using var provider = new ServiceCollection().AddPatternCatalogContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<PatternCatalogContextRepositoryFixture>();
var repository = scope.ServiceProvider.GetRequiredService<IPatternCatalog>();
fixture.Data.StoredPatternEntryStore.SeedRange([
    new(1, "Book", "Shelf A", 10), new(2, "bookmark", null, 20),
    new(3, "50% Off", "Shelf B", 30), new(4, "Hidden Book", "Shelf C", 100, true)]);
var cards = await repository.SearchAsync(new("%BOOK%"));
if (!cards.Select(card => card.Id).SequenceEqual([1, 2]) || !cards.Select(card => card.Length).SequenceEqual([4, 8]))
    throw new InvalidOperationException("The designer must translate both provider matching and the registered length function.");
if (!(await repository.SearchAsync(new("%book%", 6))).Select(card => card.Id).SequenceEqual([2]))
    throw new InvalidOperationException("The named query retains its ordinary request/visibility rules.");
if (!(await repository.SummariesAsync("shelf _")).SequenceEqual([1, 3]) || !(await repository.EscapedAsync("%!%%")).SequenceEqual([3]))
    throw new InvalidOperationException("Nullable values, wildcard units and explicit escaping have declared fixture semantics.");
if (!(await repository.TotalsAsync()).Select(total => total.Length).SequenceEqual([4, 7, 8]))
    throw new InvalidOperationException("Translated group keys retain named aggregate projections.");
var stream = new List<int>(); await foreach (var card in repository.StreamAsync(new("%book%"))) stream.Add(card.Id);
if (!stream.SequenceEqual([1, 2])) throw new InvalidOperationException("Cold streams reuse the same translated definitions.");
Console.WriteLine("MemoryTranslations: exact provider/helper translations, public mapped seeds, groups, nullable/escaped patterns and streams passed without EF/models.");
