// Consumer assertions only; configuration lives in CapturedNotesRepositoryDesigner.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MappedReturning;
using RonSijm.RepoGen.ConfigurationExamples.Models.MappedReturning;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.MappedReturning;

internal static class Program
{
    private static async Task Main()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<CapturedNotesContext>(options => options.UseSqlite(connection));
        services.AddSingleton<SqliteReturningWriter>();
        services.AddCapturedNotesContextRepositories();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CapturedNotesContext>();
        await context.Database.EnsureCreatedAsync();
        var note = new CapturedNote { Title = "first" };
        var hidden = new CapturedNote { Title = "hidden" };
        context.AddRange(note, hidden);
        context.Entry(hidden).Property("Archived").CurrentValue = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var tracked = await context.Notes.SingleAsync(row => row.Id == note.Id);
        tracked.Title = "pending edit";
        tracked["SourceTag"] = "pending tag";
        var writes = scope.ServiceProvider.GetRequiredService<ICapturedNoteWrites>();
        const string title = "'; DROP TABLE ignored; --";
        var changed = (await writes.RenameAsync(new(note.Id, title, 1))).Items.Single();
        var expectedNormalizedTitle = title.ToUpperInvariant();
        if (changed.Title != title || changed.NormalizedTitle != expectedNormalizedTitle || changed.Version != 2 || changed.SourceTag != "api")
        {
            throw new InvalidOperationException("Native results did not capture the computed field, field-only token or indexer value.");
        }
        if (tracked.Title != "pending edit" || tracked.ReadVersion() != 1 || (string?)tracked["SourceTag"] != "pending tag")
        {
            throw new InvalidOperationException("Native execution saved or synchronized pending tracked state.");
        }
        if ((await writes.RenameAsync(new(note.Id, "stale", 1))).AffectedRows != 0 || (await writes.RenameAsync(new(hidden.Id, "hidden", 1))).AffectedRows != 0)
        {
            throw new InvalidOperationException("Version predicates or the model filter were bypassed.");
        }

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var removed = (await writes.RemoveAsync(new(note.Id, "unused", 2))).Items.Single();
            if (removed != changed)
            {
                throw new InvalidOperationException("Delete did not return pre-delete mapped values.");
            }
            await transaction.RollbackAsync();
        }
        if (!await context.Notes.AsNoTracking().AnyAsync(row => row.Id == note.Id))
        {
            throw new InvalidOperationException("Caller transaction rollback did not restore the native delete.");
        }
        context.ChangeTracker.Clear();
        if ((await writes.RemoveAsync(new(note.Id, "unused", 2))).AffectedRows != 1 || await context.Notes.IgnoreQueryFilters().CountAsync() != 1)
        {
            throw new InvalidOperationException("Final delete or hidden-row isolation failed.");
        }
        Console.WriteLine("MappedReturning: field-backed values, indexer data, field-only versions, stale/hidden guards, pending isolation and rollback passed.");
    }
}
