using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.OrderReports;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryJoins;

internal static class Program
{
    private static readonly int[] JoinedIds = [1, 5];
    private static readonly int[] UnmatchedIds = [2, 3, 6];
    private static readonly string[] DirectoryNames = ["North", "South"];
    private static readonly int[] IdentifierIds = [1, 2, 3, 5, 6];

    private static async Task Main()
    {
        var services = new ServiceCollection().AddOrderReportsContextRepositoryTestDoubles();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IOrderReports>();
        var repository = scope.ServiceProvider.GetRequiredService<OrderEntryRepositoryTestDouble>();
        var accountsRepository = scope.ServiceProvider.GetRequiredService<DirectoryAccountRepositoryTestDouble>();
        Ensure(ReferenceEquals(repository.Fixture, accountsRepository.Fixture), "Repositories must share the fixture.");
        var data = repository.Fixture.Data;
        data.OrderEntryStore.SeedRange(new OrderEntry[]
        {
            new() { Id = 1, AccountId = 1, Region = "N", Quantity = 2 },
            new() { Id = 2, AccountId = 2, Region = "S", Quantity = 5 },
            new() { Id = 3, AccountId = 99, Region = "N", Quantity = 7 },
            new() { Id = 4, AccountId = 1, Region = "N", Quantity = 9, Hidden = true },
            new() { Id = 5, AccountId = 1, Region = "N", Quantity = 3 },
            new() { Id = 6, AccountId = 1, Region = "S", Quantity = 11 }
        });
        data.DirectoryAccountStore.SeedRange(new DirectoryAccount[]
        {
            new() { Id = 1, Name = "North", Region = "N" },
            new() { Id = 2, Name = "Hidden", Region = "S", Hidden = true },
            new() { Id = 3, Name = "South", Region = "S" }
        });
        data.CaptureBaseline();

        // The service-facing port contains named operations only; the designer owns joins, visibility and paging.
        Ensure((await reports.JoinedAsync()).Select(row => row.OrderId).SequenceEqual(JoinedIds), "Composite joins and both visibility policies.");
        var left = await reports.LeftJoinedAsync();
        Ensure(left.Count == 5 && left.Count(row => row.AccountName is null) == 3, "Left join retains unmatched/hidden/mismatched accounts as nulls.");
        Ensure((await reports.UnmatchedAsync()).SequenceEqual(UnmatchedIds), "Correlated anti-join.");
        var totals = await reports.TotalsAsync(new(MinimumCount: 2), 1, 10);
        Ensure(totals.TotalCount == 1 && totals.Items.Single() == new AccountOrderTotal("North", 2, 5), "Joined aggregate and counted page.");
        Ensure((await reports.DirectoryAsync()).SequenceEqual(DirectoryNames), "Replacement root uses only the account store.");
        Ensure((await reports.IdentifiersAsync()).SequenceEqual(IdentifierIds), "Union applies each source policy.");

        var stream = reports.StreamAsync();
        data.DirectoryAccountStore.Seed(new() { Id = 1, Name = "Changed before enumeration", Region = "N" },
            Testing.InMemoryDuplicateKeyBehavior.Replace);
        var streamed = new List<OrderAccountRow>();
        await using (var iterator = stream.GetAsyncEnumerator())
        {
            Ensure(await iterator.MoveNextAsync(), "Cold stream has a first row.");
            streamed.Add(iterator.Current);
            data.DirectoryAccountStore.Clear();
            while (await iterator.MoveNextAsync()) streamed.Add(iterator.Current);
        }
        Ensure(streamed.Count == 2 && streamed.All(row => row.AccountName == "Changed before enumeration"), "Both sources remain detached through enumeration.");
        data.RestoreBaseline();
        Ensure((await reports.JoinedAsync()).Count == 2, "Baseline restore covers both stores.");
        Console.WriteLine("MemoryJoins: composite/left joins, correlated anti-joins, grouped pages, unions and cold multi-store streams passed without EF.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
