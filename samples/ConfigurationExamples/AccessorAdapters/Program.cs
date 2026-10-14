// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/LookupDirectoryRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.TaskDesk;

namespace RonSijm.RepoGen.ConfigurationExamples.AccessorAdapters;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<LookupDirectoryContext>(options => options.UseSqlite(connection));
        services.AddLookupDirectoryContextSelectorRepositories();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LookupDirectoryContext>();
        await context.Database.EnsureCreatedAsync();
        var id = Guid.Parse("3bf903b5-09e6-49e1-9f66-a82df5a588a2");
        context.Entries.Add(new DirectoryEntry { Id = new StorageDirectoryId(id), Code = 42, Name = "Shared directory" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var reads = scope.ServiceProvider.GetRequiredService<IReadDirectoryEntryRepository>();
        if ((await reads.ById(id).ProjectToDirectorySummary())!.Id != id ||
            !await reads.ByCodeAndId(new DirectoryCode(42), id).ExistsAsync() ||
            (await reads.ListEntriesAsync()).Count != 1 || context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException("EF-converter/accessor adapters or CQRS detached reads failed.");
        }

        Console.WriteLine("AccessorAdapters: reversed EF ID conversion, explicit typed code and composite-index lookups, read-only detached selectors passed.");
    }
}
