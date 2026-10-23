// Consumer assertions only. Services use IVersionedNoteWrites, not EF property names or token setter lambdas.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ShadowVersions;
using RonSijm.RepoGen.ConfigurationExamples.Models.ShadowVersions;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.ShadowVersions;

internal static class Program
{
    private static async Task Main()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var services = new ServiceCollection(); services.AddDbContext<VersionedNotesContext>(options => options.UseSqlite(connection));
        services.AddSingleton<SqliteReturningWriter>(); services.AddVersionedNotesContextRepositories();
        await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VersionedNotesContext>(); await context.Database.EnsureCreatedAsync();
        var first = new VersionedNote { Title = "first" }; var second = new VersionedNote { Title = "second" }; var hidden = new VersionedNote { Title = "hidden" };
        context.AddRange(first, second, hidden); context.Entry(hidden).Property("Archived").CurrentValue = true;
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var writes = scope.ServiceProvider.GetRequiredService<IVersionedNoteWrites>();
        var reads = scope.ServiceProvider.GetRequiredService<IReadVersionedNoteRepository>();
        var changed = await writes.RenameAsync(new(first.Id, "tracked", 1));
        if (changed.Status != CommandStatus.Applied || changed.Value?.Version != 2 || (await writes.RenameAsync(new(first.Id, "stale", 1))).Status != CommandStatus.Conflict)
            throw new InvalidOperationException("Tracked writes did not check the caller's shadow version or return its saved token.");
        var native = await writes.RenameNativeAsync(new(second.Id, "native", 1));
        if (native.Items.Single().Version != 2 || (await writes.RenameNativeAsync(new(second.Id, "stale", 1))).AffectedRows != 0 || (await writes.RenameNativeAsync(new(hidden.Id, "hidden", 1))).AffectedRows != 0)
            throw new InvalidOperationException("Native token predicates or global visibility failed.");
        var rejected = await writes.RenameManyAsync(new([new(first.Id, "must not save", 2), new(second.Id, "wrong version", 99)]));
        if (rejected.Status != CommandStatus.Conflict || (await reads.FindAsync(first.Id))?.Version != 2)
            throw new InvalidOperationException("A conflicting batch partially saved its rows.");
        context.ChangeTracker.Clear();
        var batch = await writes.RenameManyAsync(new([new(second.Id, "second saved", 2), new(first.Id, "first saved", 2)]));
        if (batch.Status != CommandStatus.Applied || batch.Value is not { Count: 2 } items || items[0].Id != second.Id || items[1].Id != first.Id || items.Any(item => item.Version != 3))
            throw new InvalidOperationException("Per-item shadow tokens or ordered fresh saved results failed.");
        Console.WriteLine("ShadowVersions: EF-free ports, tracked/native shadow-token checks, stale conflicts, filtered rows and atomic bounded batches passed.");
    }
}
