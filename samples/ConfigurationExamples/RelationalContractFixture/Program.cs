using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ContractNotes;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ContractNotes;
using RonSijm.RepoGen.ConfigurationExamples.Models.ContractNotes;
using RonSijm.RepoGen.ConfigurationExamples.RelationalContracts;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ContractNotes;
using RonSijm.RepoGen.Testing;
using RonSijm.RepoGen.Testing.EntityFrameworkCore;

internal sealed class Program
{
    private static async Task Main()
    {
        var suite = NoteReadContractCases.Create();
        // Each fake case starts from an independently seeded fixture, not a shared mutable test singleton.
        await suite.RunAsync(_ =>
        {
            var fixture = new ContractNotesContextRepositoryFixture();
            fixture.Data.ContractNoteStore.SeedRange(Seeds());
            return ValueTask.FromResult(new RepositoryContractSession<IContractNotes>(new ContractNoteRepositoryTestDouble(fixture)));
        });

        await using var relational = await RelationalRepositoryFixture.CreateAsync<ContractNotesContext>(options =>
        {
            options.ConfigureServices = services =>
            {
                services.AddSingleton(_ =>
                {
                    var connection = new SqliteConnection("Data Source=:memory:");
                    connection.Open();
                    return connection; // Factory registration transfers ownership to the fixture provider.
                });
                services.AddDbContext<ContractNotesContext>((provider, builder) => builder.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
                services.AddContractNotesContextRepositories();
            };
            options.InitializeDatabaseAsync = (context, token) => context.Database.EnsureCreatedAsync(token);
            options.ResetDatabaseAsync = (context, token) => context.Set<ContractNote>().ExecuteDeleteAsync(token);
            options.SeedAsync = async (context, token) => { context.AddRange(Seeds()); await context.SaveChangesAsync(token); };
        });
        await suite.RunAsync(async token =>
        {
            await relational.ResetAsync(token);
            var session = await relational.OpenSessionAsync(token);
            try
            {
                return new RepositoryContractSession<IContractNotes>(session.GetRepository<IContractNotes>(), session.DisposeAsync);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        });
        Console.WriteLine($"RelationalContractFixture: {suite.CaseNames.Count} shared contract cases passed on generated fixtures and real SQLite repositories.");
    }

    private static ContractNote[] Seeds() => new ContractNote[]
    {
        new() { Id = 1, Name = "alpha", Category = "notes" },
        new() { Id = 2, Name = "beta", Category = "notes" },
        new() { Id = 3, Name = "hidden", Category = "notes", Hidden = true }
    };
}
