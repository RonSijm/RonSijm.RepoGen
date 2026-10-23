// Runtime usage/assertions only. Start with Configuration/ChecklistRepositoryDesigner.cs.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Collections;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Collections;
using RonSijm.RepoGen.ConfigurationExamples.Models.Collections;

namespace RonSijm.RepoGen.ConfigurationExamples.Collections;

internal static class Program
{
    private static async Task Main()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<CollectionsContext>(options => options.UseSqlite(connection));
        services.AddCollectionsContextRepositories();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CollectionsContext>();
        await context.Database.EnsureCreatedAsync();
        context.Checklists.AddRange(new Checklist { Id = 1, OwnerId = 7, Items = [new() { Key = "A", Text = "Initial", InternalNote = "Infrastructure-owned" }, new() { Key = "B", Text = "Remove explicitly" }] },
            new Checklist { Id = 2, OwnerId = 8, Items = [new() { Key = "A", Text = "Other owner" }] });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var writes = scope.ServiceProvider.GetRequiredService<IWriteChecklistRepository>();
        var saved = await writes.SaveItemsAsync(new(1, 7, 1, [new("A", "Updated"), new("C", "Added")]));
        if (!saved.Succeeded || saved.Value!.Version != 2 || !saved.Value.Items.Select(item => item.Key).SequenceEqual(["A", "C"]))
        {
            throw new InvalidOperationException("The configured replacement did not return its saved collection.");
        }

        var merged = await writes.MergeItemsAsync(new(1, 7, 2, [new("A", "Merged")]));
        var missing = await writes.SaveItemsAsync(new(1, 8, 3, []));
        var stale = await writes.SaveItemsAsync(new(1, 7, 1, []));
        if (!merged.Succeeded || missing.Status != CommandStatus.NotFound || stale.Status != CommandStatus.Conflict)
        {
            throw new InvalidOperationException("Merge, ownership or aggregate-version behavior differed from the designer.");
        }

        var items = await context.Items.AsNoTracking().ToListAsync();
        if (items.Count != 3 || items.Single(item => item.ChecklistId == 1 && item.Key == "A").InternalNote != "Infrastructure-owned" ||
            items.Single(item => item.ChecklistId == 2).Text != "Other owner" || items.All(item => item.Key != "C"))
        {
            throw new InvalidOperationException("An unconfigured field, missing merge member or another owner's item changed.");
        }

        Console.WriteLine("Collections: explicit add/update/delete, safe merge defaults, saved DTOs, owner isolation and version conflicts passed.");
    }
}
