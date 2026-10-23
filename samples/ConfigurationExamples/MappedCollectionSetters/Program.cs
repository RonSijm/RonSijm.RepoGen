using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MappedCollectionSetters;

namespace RonSijm.RepoGen.ConfigurationExamples.MappedCollectionSetters;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddReviewBoardsContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<ReviewBoardsContextRepositoryFixture>();
        var reader = scope.ServiceProvider.GetRequiredService<IReviewBoardReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IReviewBoardWriter>();
        fixture.Data.ReviewBoardStore.Seed(new(1, "Review", 1));
        fixture.Data.ReviewItemStore.SeedRange([new(1, 1, "a", "A", 0, 1, null), new(2, 1, "b", "B", 4, 2, "old")]);
        fixture.Data.CaptureBaseline();
        var stale = await writer.ReplaceAsync(new(1, "must restore", 1, [new("a", "changed", 1), new("b", "stale", 0)]));
        if (stale.Status != CommandStatus.Conflict || fixture.Data.PendingCount != 0 || (await reader.BoardsAsync()).Single().Revision != 1)
            throw new InvalidOperationException("One stale child must leave the parent and every child unchanged.");
        var merged = await writer.MergeAsync(new(1, "Reviewed", 1, [new("a", "ignored", 1, "reviewed", ChangeCaption: false)]));
        if (merged.Value is not { Revision: 2, Items: [{ Caption: "A", Edits: 1, Revision: 2, Remark: "reviewed" }, { Code: "b", Revision: 2 }] })
            throw new InvalidOperationException("Merge must keep omitted rows and apply only the explicitly configured mapped assignments.");
        await writer.StageReplaceAsync(new(1, "Staged", 2, [new("a", "staged caption", 2)]));
        if ((await reader.BoardsAsync()).Single() is not { Revision: 2, Items.Count: 2 })
            throw new InvalidOperationException("Stage cannot change saved parent/child rows.");
        await fixture.Data.SaveChangesAsync();
        if ((await reader.BoardsAsync()).Single() is not { Revision: 3, Items: [{ Caption: "staged caption", Edits: 2, Revision: 3, Remark: null }] })
            throw new InvalidOperationException("Shared Save must persist the parent, mapped child state and explicit missing-row deletion together.");
        await using (await fixture.Data.BeginTransactionAsync())
            await writer.MergeAsync(new(1, "temporary", 3, [new("a", "temporary", 3)]));
        if ((await reader.BoardsAsync()).Single().Revision != 3) throw new InvalidOperationException("Checkpoint rollback must restore every participant.");
        fixture.Data.RestoreBaseline();
        if ((await reader.BoardsAsync()).Single() is not { Revision: 1, Items.Count: 2 })
            throw new InvalidOperationException("Baseline reset must restore both complete mapped stores.");
        Console.WriteLine("MappedCollectionSetters: parent/child permissions, mapped scalars, stale versions, Keep/Delete, Stage/shared Save and checkpoints passed without EF/models.");
    }
}
