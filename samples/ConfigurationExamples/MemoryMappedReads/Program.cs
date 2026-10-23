using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedReads;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedReads;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddMappedNotebookContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<MappedNotebookContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IMappedMemoReader>();
fixture.Data.StoredMappedMemoStore.SeedRange([
    new(1, "alpha", 3, "api", false), new(2, "beta", 7, "import", false), new(3, "archived", 9, "api", true)]);
fixture.Data.CaptureBaseline();
if ((await reader.GetAsync(1)) != new MappedMemoCard(1, "alpha", 3, "api"))
{
    throw new InvalidOperationException("Read stored scalar values, not the entity getter's display prefix, default revision or indexer implementation.");
}
if (!(await reader.CardsAsync()).Select(row => row.Id).SequenceEqual([1, 2]) || await reader.GetAsync(3) is not null)
{
    throw new InvalidOperationException("The separately enabled field-only model filter must hide archived rows.");
}
if (!(await reader.SortedAsync("Caption", true)).Select(row => row.Id).SequenceEqual([2, 1]))
{
    throw new InvalidOperationException("Whitelisted dynamic ordering must use the same explicitly bound scalar.");
}
if ((await reader.RecentAsync(5)).Single().Id != 2)
{
    throw new InvalidOperationException("Named selection must reuse the field-only revision without service-side EF calls.");
}
var page = await reader.PageAsync(2, 1);
if (page.TotalCount != 2 || page.Items.Single().Id != 2)
{
    throw new InvalidOperationException("Counted pages must retain model filtering and getter-free ordering.");
}
fixture.Data.StoredMappedMemoStore.Seed(new(1, "replaced", 4, "fixture", false), InMemoryDuplicateKeyBehavior.Replace);
if ((await reader.GetAsync(1))!.Source != "fixture")
{
    throw new InvalidOperationException("A fresh snapshot must use explicit public fixture data, not tracked entity state.");
}
fixture.Data.RestoreBaseline();
if ((await reader.GetAsync(1))!.Caption != "alpha" || fixture.Data.StoredMappedMemoStore.Count != 3)
{
    throw new InvalidOperationException("Baseline restoration must retain bindings and all arrangement rows.");
}
Console.WriteLine("MemoryMappedReads: field-backed, field-only and indexer scalar reads, model filters, safe sorting and pages passed without EF/models.");
