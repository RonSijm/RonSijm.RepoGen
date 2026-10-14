using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNavigation;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNavigation;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryNavigation;

internal static class Program
{
    private static async Task Main()
    {
        using var provider = new ServiceCollection().AddNavigationCatalogContextRepositoryTestDoubles().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<CatalogGroupRepositoryTestDouble>();
        var browse = scope.ServiceProvider.GetRequiredService<ICatalogBrowse>();
        var entries = scope.ServiceProvider.GetRequiredService<CatalogEntryRepositoryTestDouble>();
        Ensure(ReferenceEquals(repository.Fixture, entries.Fixture), "All participating repositories share the same fixture.");
        var data = repository.Fixture.Data;
        data.CatalogGroupStore.SeedRange(new CatalogGroup[]
        {
            new() { Id = 1, Code = "catalog", Region = "N", Name = "North" },
            new() { Id = 2, Code = "catalog", Region = "S", Name = "South" },
            new() { Id = 3, Code = "hidden", Region = "N", Name = "Hidden", Hidden = true },
            new() { Id = 4, Code = "empty", Region = "N", Name = "Empty" }
        });
        data.CatalogCategoryStore.Seed(new() { Id = 1, Name = "Tools" });
        data.CatalogEntryStore.SeedRange(new CatalogEntry[]
        {
            new() { Id = 1, GroupCode = "catalog", GroupRegion = "N", CategoryId = 1, Title = "Hammer", Quantity = 2 },
            new() { Id = 2, GroupCode = "catalog", GroupRegion = "N", CategoryId = 1, Title = "Retired", Quantity = 99, Hidden = true },
            new() { Id = 3, GroupCode = "catalog", GroupRegion = "S", Title = "Guide", Quantity = 3 },
            new() { Id = 4, GroupCode = "hidden", GroupRegion = "N", Title = "Unlisted", Quantity = 1 },
            new() { Id = 5, GroupCode = "catalog", GroupRegion = null, Title = "Unassigned", Quantity = 1 }
        });
        data.CaptureBaseline();
        var cards = await browse.BrowseAsync();
        Ensure(cards.Count == 3 && cards[0].Entries.Single().Category == "Tools" && cards[0].Quantity == 2, "Nested relationships and explicit child filtering.");
        Ensure(cards[1].Entries.Single().Category is null && cards[2].Entries.Count == 0, "Optional references and empty collections.");
        var counts = await browse.RawCountsAsync();
        Ensure(counts[0] == 2, "A related collection does not automatically inherit the Entries root visibility policy.");
        Ensure((await browse.DetailAsync(1))!.Entries.Single().Title == "Hammer", "Named unfiltered load profile and projection.");
        var page = await browse.PageAsync(2, 1);
        Ensure(page.TotalCount == 3 && page.Items.Single().Name == "South", "Counted pages over one graph snapshot.");
        var items = await browse.ItemsAsync();
        Ensure(items.Count == 4 && items.Single(row => row.Id == 4).Group == "Hidden" && items.Single(row => row.Id == 5).Group is null,
            "From root visibility, related policy review and null composite foreign key.");
        var stream = browse.StreamAsync();
        data.CatalogCategoryStore.Seed(new() { Id = 1, Name = "Before" }, InMemoryDuplicateKeyBehavior.Replace);
        await using (var iterator = stream.GetAsyncEnumerator())
        {
            Ensure(await iterator.MoveNextAsync() && iterator.Current.Entries.Single().Category == "Before", "Cold stream loads at first move.");
            data.CatalogCategoryStore.Clear();
            while (await iterator.MoveNextAsync()) { }
        }
        data.RestoreBaseline();
        Ensure((await browse.BrowseAsync())[0].Entries.Single().Category == "Tools", "Baseline restoration includes all related stores.");
        Ensure(data.CatalogGroupStore.Find(1)!.Entries.Count == 0, "Loading never modifies the arranged rows.");
        Console.WriteLine("MemoryNavigation: declared composite relationships, nested DTOs, load profiles, visibility, pages and cold streams passed without EF.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
