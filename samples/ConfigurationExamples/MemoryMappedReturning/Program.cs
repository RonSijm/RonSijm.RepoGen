using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedReturning;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedReturning;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryMappedReturning;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddReceiptQueueContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<ReceiptQueueContextRepositoryFixture>();
        var reader = scope.ServiceProvider.GetRequiredService<IDispatchReceiptReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IDispatchReceiptWriter>();
        fixture.Data.ReceiptEnvelopeStore.Seed(new(7, "message", 2, "notification", 3));
        fixture.Data.CaptureBaseline();
        if ((await writer.AdvanceAsync(new(7, 99))).AffectedRows != 0)
        {
            throw new InvalidOperationException("A stale mapped version must not match the immediate command.");
        }
        var advanced = (await writer.AdvanceAsync(new(7, 3))).Items.Single();
        if (advanced.Revision != 4 || advanced.Category != "notification" || fixture.Data.PendingCount != 0)
        {
            throw new InvalidOperationException("Update must return fresh scalar state, without invoking the display getter or staging a save.");
        }
        var consumed = (await writer.ConsumeAsync(new(7, 4))).Items.Single();
        if (consumed != advanced || (await reader.ListAsync()).Count != 0)
        {
            throw new InvalidOperationException("Delete must return the original matched values, without advancing the revision again.");
        }
        fixture.Data.RestoreBaseline();
        await using (var checkpoint = await fixture.Data.BeginTransactionAsync())
        {
            await writer.ConsumeAsync(new(7, 3));
        }
        if ((await reader.ListAsync()).Single().Revision != 3)
        {
            throw new InvalidOperationException("Checkpoint rollback must restore consumed mapped scalar state.");
        }
        Console.WriteLine("MemoryMappedReturning: post-update/pre-delete shadow and field values, stale versions, detached receipts and rollback passed without EF/models.");
    }
}
