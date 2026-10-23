// Consumer assertions only; start with Configuration/AliasDirectoryRepositoryDesigner.cs.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.MappedAccessors.Persistence;
using RonSijm.RepoGen.ConfigurationExamples.Models.MappedAccessors;

namespace RonSijm.RepoGen.ConfigurationExamples.MappedAccessors;

internal static class Program
{
    private static async Task Main()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var services = new ServiceCollection(); services.AddDbContext<AliasDirectoryContext>(options => options.UseSqlite(connection));
        services.AddAliasDirectoryContextSelectorRepositories();
        await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AliasDirectoryContext>(); await context.Database.EnsureCreatedAsync();
        foreach (var (code, state, hidden) in new[] { ("alpha", AliasState.Enabled, false), ("beta", AliasState.Disabled, false), ("hidden", AliasState.Enabled, true) })
        {
            var entry = new AliasEntry { Name = code + " entry" }; context.Add(entry);
            context.Entry(entry).Property("SearchCode").CurrentValue = code;
            context.Entry(entry).Property("State").CurrentValue = state;
            context.Entry(entry).Property("Archived").CurrentValue = hidden;
        }
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var reads = scope.ServiceProvider.GetRequiredService<IReadAliasEntryRepository>();
        var found = await reads.BySearchCode("alpha").ProjectToAliasCard();
        var active = await reads.ByState("Enabled").ProjectToAliasCard();
        if (found?.Code != "alpha" || active.Count != 1 || active[0] != found || await reads.BySearchCode("hidden").ExistsAsync() || context.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Typed shadow accessors, reverse converter, configured DTOs or read-only filters failed.");
        if (await reads.BySearchCode("'; DROP TABLE ignored; --").ExistsAsync() || await context.Entries.IgnoreQueryFilters().CountAsync() != 3)
            throw new InvalidOperationException("Accessor values were not handled as parameterized data.");
        Console.WriteLine("MappedAccessors: explicit shadow lookups, unique metadata, converter inputs, configured DTOs and detached filtered reads passed.");
    }
}
