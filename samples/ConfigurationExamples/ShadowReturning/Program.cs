// Executable consumer assertions. Configuration lives in AuditDocumentsRepositoryDesigner, not here.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ShadowReturning;
using RonSijm.RepoGen.ConfigurationExamples.Models.ShadowReturning;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.ShadowReturning;

internal static class Program
{
    private static async Task Main()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<AuditDocumentsContext>(options => options.UseSqlite(connection));
        services.AddSingleton<SqliteReturningWriter>(); services.AddAuditDocumentsContextRepositories();
        await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDocumentsContext>();
        await context.Database.EnsureCreatedAsync();
        var active = new AuditDocument { Title = "draft" }; var archived = new AuditDocument { Title = "archive" };
        context.Documents.AddRange(active, archived); context.Entry(archived).Property("Archived").CurrentValue = true;
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var writes = scope.ServiceProvider.GetRequiredService<IAuditDocumentWrites>();

        // Pending tracked shadow edits must not leak into native returned values or be saved implicitly.
        var tracked = await context.Documents.SingleAsync(); context.Entry(tracked).Property("SourceTag").CurrentValue = "pending";
        const string title = "updated'; DROP TABLE ignored; --";
        var expectedNormalizedTitle = title.ToUpperInvariant(); // Verify the stored uppercase value, not merely case-insensitive equality.
        var changed = await writes.RenameAsync(new(active.Id, title, 1));
        var result = changed.Items.Single();
        if (result.Title != title || result.NormalizedTitle != expectedNormalizedTitle || result.TitleLength != title.Length || result.Revision != 2 || result.SourceTag != "database")
            throw new InvalidOperationException("Native returned shadow values did not reflect the updated database row.");
        if (tracked.Title != "draft" || context.Entry(tracked).Property("SourceTag").CurrentValue as string != "pending")
            throw new InvalidOperationException("The native writer unexpectedly synchronized tracked state.");
        context.ChangeTracker.Clear();

        // The same native path participates in a caller-owned transaction and returns pre-delete values.
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var removed = await writes.RemoveAsync(new(active.Id, "unused", 2));
            if (removed.Items.Single() != result) throw new InvalidOperationException("Delete did not return pre-delete values.");
            await transaction.RollbackAsync();
        }
        if ((await writes.RemoveAsync(new(archived.Id, "unused", 1))).AffectedRows != 0)
            throw new InvalidOperationException("The native source bypassed the model filter.");
        if ((await writes.RemoveAsync(new(active.Id, "unused", 1))).AffectedRows != 0)
            throw new InvalidOperationException("An outdated version was accepted.");
        if ((await writes.RemoveAsync(new(active.Id, "unused", 2))).AffectedRows != 1 || await context.Documents.IgnoreQueryFilters().CountAsync() != 1 || context.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Native delete, rollback, or detached tracking semantics failed.");
        Console.WriteLine("ShadowReturning: computed/default shadow values, versions, filters, detached state and transaction rollback passed.");
    }
}
