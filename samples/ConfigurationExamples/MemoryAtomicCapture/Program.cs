using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicCapture;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicCapture;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryAtomicCapture;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddCapturedReservationsContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<CapturedReservationsContextRepositoryFixture>();
        var writer = scope.ServiceProvider.GetRequiredService<ICapturedReservationWriter>();
        fixture.Data.CapturedReservationStore.Seed(new(1, "first", 2, "FIRST", 3, "queued"));
        fixture.Data.CaptureBaseline();
        var inserted = await writer.EnsureAsync(new(20, "new", 4));
        if (inserted.Status != CommandStatus.Inserted || inserted.Value is not { Label: "NEW", Revision: 5, Route: "queued" })
        {
            throw new InvalidOperationException("An insertion must return exact explicitly generated field/shadow/indexer scalars.");
        }
        if ((await writer.EnsureAsync(new(900, "first"))).Status != CommandStatus.AlreadyExists)
        {
            throw new InvalidOperationException("The declared unique conflict must not create a second row.");
        }
        var updated = await writer.PutAsync(new(900, "first", 2, ExpectedRevision: 3));
        if (updated.Status != CommandStatus.Updated || updated.Value is not { Id: 1, Counter: 4, Label: "FIRST", Revision: 5 })
        {
            throw new InvalidOperationException("An upsert must retain saved identity and return post-update scalar state.");
        }
        var stale = await writer.PutAsync(new(900, "first", ExpectedRevision: 3));
        if (stale.Status != CommandStatus.NotMatched || stale.HasValue)
        {
            throw new InvalidOperationException("Stale original versions must not expose an existing row.");
        }
        await using (var checkpoint = await fixture.Data.BeginTransactionAsync())
        {
            await writer.PutAsync(new(900, "first", ExpectedRevision: 5));
        }
        if (fixture.Data.CapturedReservationStore.Find(1)!.Revision != 5)
        {
            throw new InvalidOperationException("Checkpoint rollback must restore captured scalar state.");
        }
        fixture.Data.RestoreBaseline();
        if (fixture.Data.CapturedReservationStore.Find(20) is not null || fixture.Data.CapturedReservationStore.Find(1)!.Revision != 3)
        {
            throw new InvalidOperationException("The fixture baseline must restore all saved atomic branches.");
        }
        Console.WriteLine("MemoryAtomicCapture: inserted/updated mapped scalars, exact versions, immutable identity and rollback passed without EF/models.");
    }
}
