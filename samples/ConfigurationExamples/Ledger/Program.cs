// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/LedgerRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated;
using RonSijm.RepoGen.ConfigurationExamples.Models.Ledger;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Ledger;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var databaseA = new SqliteConnection("Data Source=:memory:");
        await databaseA.OpenAsync();
        await using var databaseB = new SqliteConnection("Data Source=:memory:");
        await databaseB.OpenAsync();
        var databases = new Dictionary<string, SqliteConnection>(StringComparer.Ordinal) { ["A"] = databaseA, ["B"] = databaseB };
        foreach (var connection in databases.Values)
        {
            await using var context = new LedgerContext(new DbContextOptionsBuilder<LedgerContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
        }

        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var services = new ServiceCollection();
        services.AddSingleton<IRepositoryResultCache>(new MemoryRepositoryResultCache(memory));
        services.AddSingleton(new DatabaseProvider(databases));
        services.AddSingleton<SqliteAtomicWriter>();
        services.AddScoped(provider => new KeyedRepositoryUnitOfWork<LedgerContext, string>(provider.GetRequiredService<DatabaseProvider>()));
        services.AddScoped<IKeyedDbContextLeaseProvider<LedgerContext, string>>(provider => provider.GetRequiredService<KeyedRepositoryUnitOfWork<LedgerContext, string>>());
        services.AddLedgerContextRepositories();
        await using var serviceProvider = services.BuildServiceProvider();

        // A service sees only these EF-free ports and input/output types.
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReadLedgerEntryRepository>();
            var write = scope.ServiceProvider.GetRequiredService<IWriteLedgerEntryRepository>();
            var work = scope.ServiceProvider.GetRequiredService<KeyedRepositoryUnitOfWork<LedgerContext, string>>();
            var created = await write.EnsureEntryAsync(new CreateLedgerEntry("A", "shared", "original"));
            Assert(created.Status == CommandStatus.Inserted && created.Value!.NormalizedCode == "SHARED", "Atomic insert returned store-generated fields.");
            Assert((await write.EnsureEntryAsync(new CreateLedgerEntry("A", "shared", "ignored"))).Status == CommandStatus.AlreadyExists, "Duplicate insert is not repeated.");
            var first = await read.GetCachedEntriesAsync(new LedgerRequest("A"));
            var patched = await write.PatchEntryAsync(new PatchLedgerEntry("A", first[0].Id, new Optional<string?>(null), 1));
            Assert(patched.Status == CommandStatus.Applied && patched.Value!.Version == 2, "Patch accepted specified null and original version.");
            Assert((await write.PatchEntryAsync(new PatchLedgerEntry("A", first[0].Id, new Optional<string?>("stale"), 1))).Status == CommandStatus.Conflict, "Stale patch reports a conflict.");
            Assert((await read.GetCachedEntriesAsync(new LedgerRequest("A")))[0].Text is null, "Saved patch invalidated the cached read.");
            var page = await read.GetPageAsync(new LedgerRequest("A"), 1, 10);
            Assert(page.TotalCount == 1 && page.Items.Count == 1, "Consistent page includes total and items.");
            Assert((await read.GetAfterAsync(new LedgerRequest("A"), new Abstractions.CursorPageRequest<int>(10))).Items.Count == 1, "Result cursor is generated.");
            await using (var transaction = await work.BeginTransactionAsync())
            {
                Assert((await write.StageDeletionAsync(new DeleteLedgerEntry("A", first[0].Id))).Status == CommandStatus.Staged, "Deletion can be staged.");
                await work.SaveChangesAsync();
                Assert((await read.GetCachedEntriesAsync(new LedgerRequest("A"))).Count == 0, "Transactional reads bypass the cache.");
                await transaction.RollbackAsync();
            }
        }

        // A rolled-back work is terminal; read through a fresh scope rather than reusing its tracker.
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReadLedgerEntryRepository>();
            Assert((await read.GetCachedEntriesAsync(new LedgerRequest("A"))).Count == 1, "Rollback preserved the previous cache entry.");
        }

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReadLedgerEntryRepository>();
            Assert((await read.GetCachedEntriesAsync(new LedgerRequest("B"))).Count == 0, "Actual database scope partitions the shared cache.");
        }

        // Explicit infrastructure opt-in; normal scopes above still have no save participant.
        foreach (var commit in new[] { false, true })
        {
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var read = scope.ServiceProvider.GetRequiredService<IReadLedgerEntryRepository>();
                var write = scope.ServiceProvider.GetRequiredService<IWriteLedgerEntryRepository>();
                var work = scope.ServiceProvider.GetRequiredService<KeyedRepositoryUnitOfWork<LedgerContext, string>>();
                var participant = new SaveReceiptParticipant();
                work.RegisterSaveParticipant(participant, participant); // May register before the first keyed request.
                var row = (await read.GetEntriesAsync(new LedgerRequest("A"))).Single();
                Assert(participant.Calls == 0, "Reads do not prepare infrastructure rows.");
                await using var transaction = await work.BeginTransactionAsync();
                var result = await write.PatchEntryAsync(new PatchLedgerEntry("A", row.Id,
                    new Optional<string?>(commit ? "receipt-committed" : "receipt-rolled-back"), row.Version));
                Assert(result.Status == CommandStatus.Applied && result.AffectedRows == 2,
                    "The generated saving command saved its row and the infrastructure receipt together.");
                Assert(participant.Calls == 1, "Save preparation ran once, not on staging/commit.");
                if (commit)
                {
                    await transaction.CommitAsync();
                }
                else
                {
                    await transaction.RollbackAsync();
                }
            }

            await using var verify = new LedgerContext(new DbContextOptionsBuilder<LedgerContext>().UseSqlite(databaseA).Options);
            Assert(await verify.SaveReceipts.CountAsync() == (commit ? 1 : 0), "Receipts follow the real transaction boundary.");
            Assert((await verify.Entries.SingleAsync()).Text == (commit ? "receipt-committed" : null), "Row writes share receipt commit/rollback.");
        }

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReadLedgerEntryRepository>();
            Assert((await read.GetCachedEntriesAsync(new LedgerRequest("A"))).Single().Text == "receipt-committed",
                "Participant saves retain generated cache invalidation.");
        }

        Console.WriteLine("Ledger: keyed databases, scoped cache, atomic/versioned writes, result cursor, optional save receipts and shared rollback/commit passed.");

        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}

// This adapter only stages local tracked data. Message definitions/publication are not RepoGen features.
internal sealed class SaveReceiptParticipant : IRepositorySaveParticipant<LedgerContext>
{
    public int Calls { get; private set; }

    public ValueTask SavingAsync(LedgerContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        var changed = context.ChangeTracker.Entries<LedgerEntry>()
            .Count(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        if (changed > 0)
        {
            context.SaveReceipts.Add(new LedgerSaveReceipt { ChangedEntries = changed });
        }

        return ValueTask.CompletedTask;
    }
}

// Application-owned selection and scope: RepoGen neither interprets tenancy nor accepts arbitrary connection strings.
internal sealed class DatabaseProvider(IReadOnlyDictionary<string, SqliteConnection> databases) : IKeyedDbContextLeaseProvider<LedgerContext, string>
{
    public ValueTask<DbContextLease<LedgerContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!databases.TryGetValue(key, out var connection))
        {
            throw new ArgumentException("Unknown database key.", nameof(key));
        }

        return ValueTask.FromResult(DbContextLease.Owned(new LedgerContext(new DbContextOptionsBuilder<LedgerContext>().UseSqlite(connection).Options), "ledger-database:" + key));
    }
}
