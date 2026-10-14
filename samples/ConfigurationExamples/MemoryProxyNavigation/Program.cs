using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyShelves;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ProxyShelves;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryProxyNavigation;

internal static class Program
{
    private static async Task Main()
    {
        using var provider = new ServiceCollection().AddProxyShelfContextRepositoryTestDoubles().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var browse = scope.ServiceProvider.GetRequiredService<IShelfCatalog>();
        var data = scope.ServiceProvider.GetRequiredService<ProxyShelfContextRepositoryFixture>().Data;
        data.ShelfRecordStore.SeedRange(new ShelfSeed[]
        {
            new(1, "shelf", "N", "North"), new(2, "shelf", "S", "South"),
            new(3, "hidden", "N", "Hidden", true), new(4, "empty", "N", "Empty")
        });
        data.ShelfCategoryRecordStore.Seed(new(1, "Tools"));
        data.ShelfItemRecordStore.SeedRange(new ShelfItemSeed[]
        {
            new(1, "shelf", "N", 1, "Hammer", 2), new(2, "shelf", "N", 1, "Retired", 99, true),
            new(3, "shelf", "S", null, "Guide", 3), new(4, "hidden", "N", null, "Unlisted", 1),
            new(5, "shelf", null, null, "Unassigned", 1)
        });
        data.CaptureBaseline();
        var cards = await browse.BrowseAsync();
        Ensure(cards.Count == 3 && cards[0].Items.Single().Category == "Tools", "Nested mapped references and explicit child filtering.");
        Ensure(cards[1].Items.Single().Category is null && cards[2].Items.Count == 0, "Optional references and empty collections.");
        Ensure((await browse.RawCountsAsync())[0] == 2, "Related collections do not inherit root policies.");
        Ensure((await browse.DetailAsync(1))!.Items.Single().Name == "Hammer", "Explicit unfiltered profile uses the same mapped graph.");
        cards[0].Items.Clear();
        Ensure((await browse.BrowseAsync())[0].Items.Count == 1, "DTO mutation never changes fixture rows.");
        var page = await browse.PageAsync(2, 1);
        Ensure(page.TotalCount == 3 && page.Items.Single().Name == "South", "Counted pages over mapped snapshots.");
        var items = await browse.ItemsAsync();
        Ensure(items.Count == 4 && items.Single(item => item.Id == 4).Shelf == "Hidden" && items.Single(item => item.Id == 5).Shelf is null,
            "From root visibility and composite null keys remain explicit.");
        var stream = browse.StreamAsync();
        data.ShelfCategoryRecordStore.Seed(new(1, "Before"), InMemoryDuplicateKeyBehavior.Replace);
        await using (var iterator = stream.GetAsyncEnumerator())
        {
            Ensure(await iterator.MoveNextAsync() && iterator.Current.Items.Single().Category == "Before", "Cold streams map at first move.");
            data.ShelfCategoryRecordStore.Clear(); while (await iterator.MoveNextAsync()) { }
        }
        data.RestoreBaseline();
        Ensure((await browse.BrowseAsync())[0].Items.Single().Category == "Tools", "Related stores restore together.");
        Console.WriteLine("MemoryProxyNavigation: internal/read-only EF graphs, public immutable seeds, nested DTOs, composite keys, visibility, paging and cold streams passed without EF/models.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
