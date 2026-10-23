using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedCollections;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedCollections;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryMappedCollections;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddVersionedChecklistContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<VersionedChecklistContextRepositoryFixture>();
        var writer = scope.ServiceProvider.GetRequiredService<IVersionedChecklistWriter>();
        var reader = scope.ServiceProvider.GetRequiredService<IVersionedChecklistItems>();
        fixture.Data.VersionedChecklistStore.Seed(new(1, "original", 2, 3));
        fixture.Data.VersionedChecklistItemStore.SeedRange([new(1, 1, "a", "old a", 2, 3), new(2, 1, "b", "old b", 2, 3)]);
        fixture.Data.CaptureBaseline();
        var request = new VersionedChecklistChange(1, "changed", 3, [new(1, "a", "changed a", 3), new(3, "c", "new c", 0)]);
        if ((await writer.ReplaceAsync(request with { ExpectedRevision = 99 })).Status != CommandStatus.Conflict)
        {
            throw new InvalidOperationException("A stale parent must reject the entire bounded reconciliation.");
        }
        var staged = await writer.StageAsync(request);
        if (staged.Status != CommandStatus.Staged || (await reader.ListAsync(1)).Any(row => row.Code == "c"))
        {
            throw new InvalidOperationException("Stage must not publish children or advance saved versions.");
        }
        await fixture.Data.SaveChangesAsync();
        var items = await reader.ListAsync(1);
        if (!items.Select(row => row.Code).SequenceEqual(["a", "c"]) || !items.Select(row => row.Revision).SequenceEqual([4, 1]) ||
            fixture.Data.VersionedChecklistStore.Find(1)!.Revision != 4)
        {
            throw new InvalidOperationException("Shared Save must publish owner-scoped additions, changes and removals with explicit fresh revisions.");
        }
        fixture.Data.RestoreBaseline();
        await using (var checkpoint = await fixture.Data.BeginTransactionAsync())
        {
            var saved = await writer.ReplaceAsync(request);
            if (saved.Value != 4)
            {
                throw new InvalidOperationException("The named saving command must return its mapped parent scalar.");
            }
        }
        if ((await reader.ListAsync(1)).Single(row => row.Code == "b").Revision != 3)
        {
            throw new InvalidOperationException("Checkpoint rollback must restore both mapped stores.");
        }
        Console.WriteLine("MemoryMappedCollections: parent/child shadow versions, bounded reconciliation, Stage/shared Save and rollback passed without EF/models.");
    }
}
