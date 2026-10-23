using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedBatchSetters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MappedBatchSetters;

namespace RonSijm.RepoGen.ConfigurationExamples.MappedBatchSetters;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddReviewEntriesContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<ReviewEntriesContextRepositoryFixture>();
        var reader = scope.ServiceProvider.GetRequiredService<IReviewEntryReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IReviewEntryWriter>();
        fixture.Data.ReviewEntryStore.SeedRange([new(1, "First", "one", 0, 1, null), new(2, "Second", "two", 4, 5, "old")]);
        fixture.Data.CaptureBaseline();
        var stale = await writer.ReviseAsync(new([new(1, "must restore", 1), new(2, "stale", 0)]));
        if (stale.Status != CommandStatus.Conflict || fixture.Data.PendingCount != 0 || (await reader.CardsAsync())[0].Edits != 0)
            throw new InvalidOperationException("A stale later item must restore every earlier mapped assignment.");
        var result = await writer.ReviseAsync(new([new(2, "updated", 5), new(1, "ignored", 1, "reviewed", ChangeCaption: false)]));
        if (result.Status != CommandStatus.Applied || result.Value is not [
                { Id: 2, Caption: "updated", Edits: 5, Revision: 6, Remark: null },
                { Id: 1, Caption: "one", Edits: 1, Revision: 2, Remark: "reviewed" }])
            throw new InvalidOperationException("Saved results must preserve input order, conditional captions and explicit token/counter advancement.");
        await writer.StageReviseAsync(new([new(1, "staged", 2), new(2, "staged too", 6)]));
        if ((await reader.CardsAsync())[0].Caption != "one") throw new InvalidOperationException("Stage cannot change saved data.");
        await fixture.Data.SaveChangesAsync();
        if ((await reader.CardsAsync()) is not [{ Caption: "staged", Revision: 3 }, { Caption: "staged too", Revision: 7 }])
            throw new InvalidOperationException("One shared save must persist the complete staged batch.");
        await using (await fixture.Data.BeginTransactionAsync())
            await writer.ReviseAsync(new([new(1, "temporary", 3)]));
        if ((await reader.CardsAsync())[0].Caption != "staged") throw new InvalidOperationException("Checkpoint rollback must restore mapped values.");
        fixture.Data.RestoreBaseline();
        if ((await reader.CardsAsync())[0].Edits != 0) throw new InvalidOperationException("Baseline restore must clear batch state.");
        Console.WriteLine("MappedBatchSetters: bounded field/shadow/indexer batches, stale-item restoration, input-ordered results and Stage/shared Save passed without EF/models.");
    }
}
