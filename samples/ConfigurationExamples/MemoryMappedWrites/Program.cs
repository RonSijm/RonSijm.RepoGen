using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedWrites;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedWrites;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryMappedWrites;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddRevisionNotebookContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<RevisionNotebookContextRepositoryFixture>();
        var reader = scope.ServiceProvider.GetRequiredService<IRevisionNoteReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IRevisionNoteWriter>();
        fixture.Data.RevisionNoteStore.Seed(new(7, "original", 2, 3));
        fixture.Data.CaptureBaseline();
        var stale = await writer.RenameAsync(new(7, "stale", 99));
        if (stale.Status != CommandStatus.Conflict || fixture.Data.PendingCount != 0)
        {
            throw new InvalidOperationException("The explicit shadow version must be checked before staging any changes.");
        }
        var staged = await writer.StageRenameAsync(new(7, "staged", 3));
        if (staged.Status != CommandStatus.Staged || (await reader.ListAsync()).Single().Revision != 3)
        {
            throw new InvalidOperationException("Stage must retain saved state; a fixture factory does not run as an implicit save.");
        }
        await fixture.Data.SaveChangesAsync();
        var saved = (await reader.ListAsync()).Single();
        if (saved.Title != "staged" || saved.Revision != 4)
        {
            throw new InvalidOperationException("Shared Save must validate and publish the explicitly generated mapped scalar.");
        }
        var renamed = await writer.RenameAsync(new(7, "saved", 4));
        if (renamed.Value?.Revision != 5)
        {
            throw new InvalidOperationException("ReturnsSaved must read the fresh mapped revision, not the old selected value.");
        }
        fixture.Data.RestoreBaseline();
        if ((await reader.ListAsync()).Single().Revision != 3)
        {
            throw new InvalidOperationException("Fixture baselines must include the explicit mapped scalar state.");
        }
        Console.WriteLine("MemoryMappedWrites: explicit shadow state, expected versions, Stage/shared Save, fresh saved results and baselines passed without EF/models.");
    }
}
