// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/PrimaryNoteRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.MultiContext;
using RonSijm.RepoGen.ConfigurationExamples.MultiContext.Primary;
using ArchiveRegistration = RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ArchiveNotes.ArchiveNoteContextRepositoryRegistration;

namespace RonSijm.RepoGen.ConfigurationExamples.MultiContext;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var primaryConnection = new SqliteConnection("Data Source=:memory:");
        await primaryConnection.OpenAsync();
        await using var archiveConnection = new SqliteConnection("Data Source=:memory:");
        await archiveConnection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<PrimaryNoteContext>(options => options.UseSqlite(primaryConnection));
        services.AddDbContext<ArchiveNoteContext>(options => options.UseSqlite(archiveConnection));
        services.AddPrimaryNoteContextSelectorRepositories();
        ArchiveRegistration.AddArchiveNoteContextRepositories(services);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Infrastructure only: setup two genuinely separate databases containing the same entity/key.
        var primary = scope.ServiceProvider.GetRequiredService<PrimaryNoteContext>();
        var archive = scope.ServiceProvider.GetRequiredService<ArchiveNoteContext>();
        await primary.Database.EnsureCreatedAsync();
        await archive.Database.EnsureCreatedAsync();
        primary.Notes.Add(new StoredNote { Id = 1, Text = "Primary", Active = true });
        archive.Notes.Add(new StoredNote { Id = 1, Text = "Archive", Active = false });
        await primary.SaveChangesAsync();
        await archive.SaveChangesAsync();
        primary.ChangeTracker.Clear();
        archive.ChangeTracker.Clear();

        await RunService(scope.ServiceProvider.GetRequiredService<IReadStoredNoteRepository>(),
            scope.ServiceProvider.GetRequiredService<IWriteStoredNoteRepository>(),
            scope.ServiceProvider.GetRequiredService<INoteArchiveReader>());
        if (archive.ChangeTracker.Entries().Any() || await archive.Notes.CountAsync() != 1)
        {
            throw new InvalidOperationException("Archive reads tracked entities or the primary save crossed contexts.");
        }

        Console.WriteLine("MultiContext: shared entity, isolated generated namespaces, CQRS + EF-free archive ports, independent databases/saves passed.");

        static async Task RunService(IReadStoredNoteRepository reads, IWriteStoredNoteRepository writes, INoteArchiveReader archive)
        {
            if ((await reads.ListActiveAsync()).Single().Text != "Primary" || (await archive.ListNotesAsync()).Single().Text != "Archive")
            {
                throw new InvalidOperationException("Context-specific named operations resolved the wrong database.");
            }

            writes.Add(new StoredNote { Id = 2, Text = "Second primary", Active = true });
            await writes.SaveChangesAsync();
            if ((await reads.ListActiveAsync()).Count != 2 || (await archive.ListNotesAsync()).Count != 1)
            {
                throw new InvalidOperationException("Writes did not stay in their configured context.");
            }
        }
    }
}
