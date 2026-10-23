using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryShadowReads;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryShadowReads;
using RonSijm.RepoGen.Testing;

await using var provider = new ServiceCollection().AddRankedNotebookContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<RankedNotebookContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IRankedNoteReader>();
fixture.Data.StoredRankedNoteStore.SeedRange([
    new(1, "high", false, 20, "notes", false), new(2, "low", false, 10, "notes", false),
    new(3, "deleted", false, 5, "archive", true), new(4, "hidden", true, 30, "notes", false)]);
fixture.Data.CaptureBaseline();
if (!(await reader.CardsAsync(15)).Select(row => row.Id).SequenceEqual([1]))
{
    throw new InvalidOperationException("The named filter must read the explicit fixture Rank, model filter and designer visibility.");
}
if (!(await reader.IncludingDeletedAsync()).Select(row => row.Id).SequenceEqual([3, 2, 1]))
{
    throw new InvalidOperationException("Ignoring the shadow Deleted filter must retain Hidden visibility and shadow Rank ordering.");
}
var page = await reader.PageAsync(2, 1);
if (page.TotalCount != 2 || page.Items.Single().Id != 1)
{
    throw new InvalidOperationException("Counted pages must use the same explicit shadow values and visibility.");
}
if ((await reader.TotalsAsync()).Single() != new RankedCategoryTotal("notes", 2, 30))
{
    throw new InvalidOperationException("Grouping and aggregation must use the mapped model-only scalars.");
}
await using (var enumerator = reader.StreamAsync().GetAsyncEnumerator())
{
    if (!await enumerator.MoveNextAsync() || enumerator.Current.Id != 2)
    {
        throw new InvalidOperationException("The cold stream must start with the lowest visible rank.");
    }
    fixture.Data.StoredRankedNoteStore.Seed(new(1, "now deleted", false, 99, "notes", true), InMemoryDuplicateKeyBehavior.Replace);
    if (!await enumerator.MoveNextAsync() || enumerator.Current.Rank != 20)
    {
        throw new InvalidOperationException("A running enumeration must keep its detached shadow-value snapshot.");
    }
}
if ((await reader.CardsAsync(0)).Count != 1 || fixture.Data.StoredRankedNoteStore.Count != 4)
{
    throw new InvalidOperationException("Fresh read filtering must not remove fixture data.");
}
fixture.Data.RestoreBaseline();
if ((await reader.CardsAsync(0)).Count != 2)
{
    throw new InvalidOperationException("Baseline restoration must retain the configured shadow reads.");
}
Console.WriteLine("MemoryShadowReads: explicit shadow scalars, model filters, named reads, sorting, pages, groups and detached streams passed without EF/models.");
