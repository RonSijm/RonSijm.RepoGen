using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelFilters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryModelFilters;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddFilteredNotebookContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<FilteredNotebookContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IFilteredNoteReader>();
fixture.Data.StoredFilteredNoteStore.SeedRange([new(1, "active", false, false, 1), new(2, "deleted", true, false, 1),
    new(3, "hidden", false, true, 1), new(4, "archived group", false, false, 2)]);
fixture.Data.StoredFilteredGroupStore.SeedRange([new(1, false), new(2, true)]);
fixture.Data.CaptureBaseline();
if (!(await reader.CardsAsync()).Select(card => card.Id).SequenceEqual([1, 4])) throw new InvalidOperationException("The model's Deleted filter and designer Hidden visibility must both apply.");
if (!(await reader.IncludingDeletedAsync()).Select(card => card.Id).SequenceEqual([1, 2, 4])) throw new InvalidOperationException("Ignoring model filters must retain designer visibility.");
if (!(await reader.IncludingHiddenAsync()).Select(card => card.Id).SequenceEqual([1, 3, 4])) throw new InvalidOperationException("Bypassing designer visibility must retain the model filter.");
if (!(await reader.ActiveGroupsAsync()).Select(card => card.Id).SequenceEqual([1])) throw new InvalidOperationException("Both From datasets must be filtered before their join.");
var page = await reader.PageAsync(2, 1);
if (page.TotalCount != 2 || page.Items.Single().Id != 4) throw new InvalidOperationException("The count and page must use filtered rows.");
await using var stream = reader.StreamAsync().GetAsyncEnumerator();
if (!await stream.MoveNextAsync()) throw new InvalidOperationException("The cold stream must return its first card.");
fixture.Data.StoredFilteredNoteStore.Seed(new(4, "newly deleted", true, false, 2), InMemoryDuplicateKeyBehavior.Replace);
if (!await stream.MoveNextAsync() || stream.Current.Id != 4) throw new InvalidOperationException("An existing enumeration must retain its detached filtered snapshot.");
if ((await reader.CardsAsync()).Count != 1 || fixture.Data.StoredFilteredNoteStore.Count != 4) throw new InvalidOperationException("Fresh filtering must not delete fixture rows.");
fixture.Data.RestoreBaseline();
if ((await reader.CardsAsync()).Count != 2) throw new InvalidOperationException("Baseline restore must retain filter behavior.");
Console.WriteLine("MemoryModelFilters: optional final-model row filters, independent visibility, filtered From joins, pages, cold streams and baseline isolation passed without EF/models.");
