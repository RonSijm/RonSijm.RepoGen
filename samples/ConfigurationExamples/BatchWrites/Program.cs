using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.BatchWrites;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.BatchWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.BatchWrites;

namespace RonSijm.RepoGen.ConfigurationExamples.BatchWrites;

internal static class Program
{
    private static async Task Main()
    {
        // Runtime consumer only: its API is declared by Configuration/BatchNotesRepositoryDesigner.cs.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<BatchWritesContext>(options => options.UseSqlite(connection));
        services.AddBatchWritesContextRepositories();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BatchWritesContext>();
        await context.Database.EnsureCreatedAsync();
        // Only demo bootstrap uses entities; all consumer operations below use designed contracts.
        context.Notes.AddRange(new BatchNote { Code = "A", OwnerId = 7, Title = "First" },
            new BatchNote { Code = "B", OwnerId = 7, Title = "Second" },
            new BatchNote { Code = "Z", OwnerId = 8, Title = "Other owner" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var writes = scope.ServiceProvider.GetRequiredService<IWriteBatchNoteRepository>();
        var reads = scope.ServiceProvider.GetRequiredService<IReadBatchNoteRepository>();
        var renamed = await writes.RenameNotesAsync(new(7, [new("B", new Optional<string?>(null), 1), new("A", new("Renamed"), 1)]));
        Require(renamed.Value is [{ Code: "B", Title: null, Version: 2 }, { Code: "A", Title: "Renamed", Version: 2 }], "Saved results follow input order, including supplied null.");
        var conflict = await writes.RenameNotesAsync(new(7, [new("A", new("Must restore"), 2), new("B", new("Stale"), 1)]));
        Require(conflict.Status == CommandStatus.Conflict && (await reads.ListNotesAsync(7))[0].Title == "Renamed", "One stale item restores the entire batch.");
        var wrongOwner = await writes.RenameNotesAsync(new(7, [new("A", new("No"), 2), new("Z", new("No"), 1)]));
        Require(wrongOwner.Status == CommandStatus.NotFound, "A mixed-owner batch never partially applies.");
        var staged = await writes.StageRenamesAsync(new(7, [new("A", new("Staged title"), 2)]));
        Require(staged.Status == CommandStatus.Staged && (await reads.ListNotesAsync(7))[0].Title == "Renamed", "Stage does not save.");
        // Saving a disjoint batch also saves pending tracked changes on this shared context.
        var deleted = await writes.DeleteNotesAsync(new(7, [new("B", default, 2)]));
        Require(deleted.Value is [{ Code: "B", Title: null }] && deleted.AffectedRows == 2, "The save includes the staged A patch and tracked B deletion.");
        Require((await reads.ListNotesAsync(7)) is [{ Code: "A", Title: "Staged title", Version: 3 }], "The final owner-scoped view is correct.");
        Require((await reads.ListNotesAsync(8)) is [{ Code: "Z", Title: "Other owner", Version: 1 }], "The other owner's row is untouched.");
        Console.WriteLine("BatchWrites: bounded keyed patches/deletes, per-item versions, supplied nulls, failure restoration and one shared save passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
