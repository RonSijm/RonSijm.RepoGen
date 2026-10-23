using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedSetters;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MappedSetters;

namespace RonSijm.RepoGen.ConfigurationExamples.MappedSetters;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddEditableNotesContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<EditableNotesContextRepositoryFixture>();
        var reader = scope.ServiceProvider.GetRequiredService<IEditableNoteReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IEditableNoteWriter>();
        fixture.Data.EditableNoteStore.Seed(new(1, "Notes", "original", 1, null));
        fixture.Data.CaptureBaseline();
        var conditional = await writer.EditAsync(new(1, "ignored", "reviewed", 1, ChangeCaption: false));
        if (conditional.Status != CommandStatus.Applied || conditional.Value!.Caption != "original" || conditional.Value.Revision != 2)
            throw new InvalidOperationException("The designer condition must preserve the caption while deliberately advancing the original token.");
        await writer.StageEditAsync(new(1, "staged", null, 2));
        if ((await reader.CardsAsync()).Single().Revision != 2)
            throw new InvalidOperationException("Staging must not change saved public data.");
        await fixture.Data.SaveChangesAsync();
        if ((await reader.CardsAsync()).Single() is not { Caption: "staged", Revision: 3, Remark: null })
            throw new InvalidOperationException("One shared save must persist all mapped scalar assignments, including null indexer state.");
        if (await writer.ExecuteEditAsync(new(1, "immediate", "saved", 3)) != 1 ||
            await writer.ExecuteEditAsync(new(1, "stale", null, 3)) != 0)
            throw new InvalidOperationException("Set-based mapped updates must honor original versions without shared Save.");
        await using (await fixture.Data.BeginTransactionAsync())
            await writer.ExecuteEditAsync(new(1, "temporary", null, 4));
        if ((await reader.CardsAsync()).Single().Caption != "immediate")
            throw new InvalidOperationException("Checkpoint rollback must restore mapped state.");
        fixture.Data.RestoreBaseline();
        if ((await reader.CardsAsync()).Single().Revision != 1)
            throw new InvalidOperationException("Baseline restore must preserve original fixture data.");
        Console.WriteLine("MappedSetters: conditional private-field, shadow-token and indexer writes, Stage/shared Save, immediate versions and rollback passed without EF/models.");
    }
}
