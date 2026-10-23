using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeResults;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNativeResults;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryNativeResults;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddQueueResultsContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<QueueResultsContextRepositoryFixture>();
        var writer = scope.ServiceProvider.GetRequiredService<IQueueTicketWriter>();
        var reader = scope.ServiceProvider.GetRequiredService<ITicketQueueReader>();
        fixture.Data.TicketQueueStore.Seed(new(1, "Support"));
        fixture.Data.QueueTicketStore.SeedRange([new(1, 1, "a", "A", 1), new(2, 1, "b", "B", 1)]);
        fixture.Data.CaptureBaseline();
        if (await reader.NameAsync(1) != "Support")
        {
            throw new InvalidOperationException("The related read port must use the same fixture partition.");
        }
        var rejected = false;
        try
        {
            await writer.RelabelAtMostOneAsync(new(1, "not saved:"));
        }
        catch (InvalidOperationException failure) when (failure.Message.Contains("row bound", StringComparison.Ordinal))
        {
            rejected = true;
        }
        if (!rejected || fixture.Data.QueueTicketStore.Snapshot().Any(row => row.Revision != 1))
        {
            throw new InvalidOperationException("The row bound must abort the entire operation, not truncate results or partly update the store.");
        }
        var result = await writer.RelabelAsync(new(1, "new:"));
        if (result.AffectedRows != 2 || result.Items.Any(item => item.Revision != 2 || item.Queue != "Support" ||
            !item.Labels.SequenceEqual(["new:a", "new:b"])))
        {
            throw new InvalidOperationException("Every fresh DTO must see the complete updated root overlay and saved related rows.");
        }
        if ((await writer.RelabelAsync(new(1, "stale:"))).AffectedRows != 0)
        {
            throw new InvalidOperationException("An unmatched original revision must return no existing-row results.");
        }
        await using (await fixture.Data.BeginTransactionAsync())
        {
            await writer.RelabelAsync(new(1, "temporary:", ExpectedRevision: 2));
        }
        if (fixture.Data.QueueTicketStore.Find(1)!.Label != "new:a")
        {
            throw new InvalidOperationException("Checkpoint disposal must restore the previous saved update.");
        }
        fixture.Data.RestoreBaseline();
        if (fixture.Data.QueueTicketStore.Find(1)!.Label != "A")
        {
            throw new InvalidOperationException("Baseline restore must retain the original arrangement.");
        }
        Console.WriteLine("MemoryNativeResults: bounded fresh DTO graphs, complete-key results, original versions and rollback passed without EF/models.");
    }
}
