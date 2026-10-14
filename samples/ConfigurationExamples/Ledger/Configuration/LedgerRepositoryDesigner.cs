// Paired with ../Model/LedgerContext.cs by ../../GenerationRunner/Program.cs (PerContract layout).
// Read API: ../../Contracts/Generated/Ledger/RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated.IReadLedgerEntryRepository.g.cs (plus write/combined contracts).
// Implementation: ../Generated/RonSijm.RepoGen.ConfigurationExamples.Models.Ledger.LedgerEntry.Repository.g.cs.
// See ../README.md for exact filenames and the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Ledger;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class LedgerRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated")
            .GenerateOnlyConfiguredEntities()
            .UseKeyedContextLeases<string>()
            .UseClosedCaching()
            .UseAtomicWrites<SqliteAtomicWriter>()
            .UseCommandTimeout(5));

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasVisibilityPolicy("Live", entry => !entry.IsDeleted);
            entity.HasQuery("GetEntries").UseContextKey<LedgerRequest, string>(request => request.Database)
                .Select(entry => new LedgerRow { Id = entry.Id, Code = entry.Code, Text = entry.Text, Version = entry.Version, NormalizedCode = entry.NormalizedCode })
                .OrderBy(entry => entry.Id).List();
            // Cache is declared on the query, never selected by a service-side flag.
            entity.HasQuery("GetCachedEntries").UseContextKey<LedgerRequest, string>(request => request.Database)
                .Cache(cache => cache.For(TimeSpan.FromMinutes(5)))
                .Select(entry => new LedgerRow { Id = entry.Id, Code = entry.Code, Text = entry.Text, Version = entry.Version, NormalizedCode = entry.NormalizedCode })
                .OrderBy(entry => entry.Id).List();
            entity.HasQuery("GetPage").UseContextKey<LedgerRequest, string>(request => request.Database)
                .Select(entry => new LedgerRow { Id = entry.Id, Code = entry.Code, Text = entry.Text, Version = entry.Version, NormalizedCode = entry.NormalizedCode })
                .OrderBy(entry => entry.Id).RequirePagingOrder().Paged(RepositoryPageConsistency.Serializable);
            entity.HasQuery("GetAfter").UseContextKey<LedgerRequest, string>(request => request.Database)
                .Select(entry => new LedgerRow { Id = entry.Id, Code = entry.Code, Text = entry.Text, Version = entry.Version, NormalizedCode = entry.NormalizedCode })
                .OrderBy(entry => entry.Id).CursorPaged<int>((entry, cursor) => entry.Id > cursor, entry => entry.Id);

            entity.HasCommand("EnsureEntry").UseContextKey<CreateLedgerEntry, string>(request => request.Database)
                .Validate<CreateLedgerEntry>(request => request.Code.Length > 0, "Code cannot be empty.")
                .Create<CreateLedgerEntry>(request => new LedgerEntry { Code = request.Code, Text = request.Text, Version = 1 })
                .AllowWrite(entry => entry.Version).InsertIfAbsent(entry => entry.Code)
                .Returns(entry => new LedgerRow { Id = entry.Id, Code = entry.Code, Text = entry.Text, Version = entry.Version, NormalizedCode = entry.NormalizedCode });
            entity.HasCommand("PatchEntry").UseContextKey<PatchLedgerEntry, string>(request => request.Database)
                .Where<PatchLedgerEntry>((entry, request) => entry.Id == request.Id)
                .Patch(setters => setters
                    .SetPropertyWhen<string?, PatchLedgerEntry>(entry => entry.Text, (entry, request) => request.Text.Value, request => request.Text.IsSpecified)
                    .SetPropertyWhen<int, PatchLedgerEntry>(entry => entry.Version, (entry, request) => entry.Version + 1, request => request.Text.IsSpecified))
                .AllowWrite(entry => entry.Version)
                .ExpectVersion<int, PatchLedgerEntry>(entry => entry.Version, (entry, request) => request.ExpectedVersion)
                .Save().Returns(entry => new LedgerRow { Id = entry.Id, Code = entry.Code, Text = entry.Text, Version = entry.Version, NormalizedCode = entry.NormalizedCode });
            entity.HasCommand("StageDeletion").UseContextKey<DeleteLedgerEntry, string>(request => request.Database)
                .Where<DeleteLedgerEntry>((entry, request) => entry.Id == request.Id).TrackedDelete().Stage();
        });
    }
}
