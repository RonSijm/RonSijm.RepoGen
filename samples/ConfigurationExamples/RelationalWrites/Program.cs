using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.FixtureTickets;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.FixtureTickets;
using RonSijm.RepoGen.ConfigurationExamples.Models.FixtureTickets;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Testing.EntityFrameworkCore;

internal sealed class Program
{
    private static async Task Main()
    {
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<FixtureTicketsContext>(options =>
        {
            options.ConfigureServices = services =>
            {
                services.AddSingleton(_ =>
                {
                    var connection = new SqliteConnection("Data Source=:memory:");
                    connection.Open();
                    return connection;
                });
                services.AddDbContext<FixtureTicketsContext>((provider, builder) => builder.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
                services.AddScoped(provider => new RepositoryUnitOfWork<FixtureTicketsContext>(provider.GetRequiredService<FixtureTicketsContext>()));
                services.AddScoped<IDbContextLeaseProvider<FixtureTicketsContext>>(provider => provider.GetRequiredService<RepositoryUnitOfWork<FixtureTicketsContext>>());
                services.AddScoped<IRepositoryUnitOfWork>(provider => provider.GetRequiredService<RepositoryUnitOfWork<FixtureTicketsContext>>());
                services.AddFixtureTicketsContextRepositories();
            };
            options.InitializeDatabaseAsync = (context, token) => context.Database.EnsureCreatedAsync(token);
        });

        int ticketId;
        await using (var session = await fixture.OpenSessionAsync())
        {
            var created = await session.GetRepository<IWriteFixtureTicketRepository>().CreateAsync(new CreateTicket("original"));
            Ensure(created.Status == CommandStatus.Applied && created.HasValue && created.Value!.Id > 0, "Real database-generated ID and saved DTO.");
            ticketId = created.Value!.Id;
        }
        await using (var session = await fixture.OpenSessionAsync())
        {
            var work = session.Services.GetRequiredService<IRepositoryUnitOfWork>();
            await using var transaction = await work.BeginTransactionAsync();
            Ensure((await session.GetRepository<IWriteFixtureTicketRepository>().StageRenameAsync(new(ticketId, "rolled back", 1))).Status == CommandStatus.Staged, "Stage parent patch.");
            Ensure((await session.GetRepository<IWriteFixtureAuditRepository>().StageAuditAsync(new(ticketId, "rolled back audit"))).Status == CommandStatus.Staged, "Stage second repository.");
            Ensure(await work.SaveChangesAsync() == 2, "One shared save persists both tracked rows, not one repository only.");
            await transaction.RollbackAsync();
        }
        await using (var session = await fixture.OpenSessionAsync())
        {
            var current = await session.GetRepository<IReadFixtureTicketRepository>().FindAsync(ticketId);
            Ensure(current == new TicketSummary(ticketId, "original", 1), "Rollback is verified in a fresh context.");
            Ensure(!await session.Context.Set<FixtureAudit>().AnyAsync(), "Rollback includes the other repository.");
            var writes = session.GetRepository<IWriteFixtureTicketRepository>();
            Ensure((await writes.StageRenameAsync(new(ticketId, "stale", 0))).Status == CommandStatus.Conflict, "Original expected version remains authoritative.");
            Ensure((await writes.StageRenameAsync(new(ticketId, "committed", 1))).Status == CommandStatus.Staged, "Valid patch stages.");
            await session.GetRepository<IWriteFixtureAuditRepository>().StageAuditAsync(new(ticketId, "committed audit"));
            Ensure(await session.Services.GetRequiredService<IRepositoryUnitOfWork>().SaveChangesAsync() == 2, "Shared save after the conflict.");
        }
        await using (var session = await fixture.OpenSessionAsync())
        {
            Ensure(await session.GetRepository<IReadFixtureTicketRepository>().FindAsync(ticketId) == new TicketSummary(ticketId, "committed", 2), "Committed values in a fresh context.");
            Ensure(await session.Context.Set<FixtureAudit>().CountAsync() == 1, "Only committed audit remains.");
        }
        Console.WriteLine("RelationalWrites: generated IDs, version conflicts, Stage/shared Save, cross-repository rollback and fresh-scope verification passed on real SQLite.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
