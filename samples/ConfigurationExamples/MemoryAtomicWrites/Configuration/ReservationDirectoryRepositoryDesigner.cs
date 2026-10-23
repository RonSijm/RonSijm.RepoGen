// ReservationDirectoryContext -> Contracts/Generated/ReservationDirectoryRepositories.g.cs and this example's three artifacts.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicWrites;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ReservationDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryAtomicWrites")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryAtomicWrites")
            .UseAtomicWrites<SqliteAtomicWriter>() // Production capability, not a fake consumer dependency.
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicWrites")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<ReservationSlot>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<SlotSeed, int>(row => row.Id,
                    row => new(row.Id, row.Scope, row.Code, row.Value, row.Version, row.Hidden, row.FixtureNote))
                .MapInMemoryEntity<SlotSeed>(row => new ReservationSlot
                    { Id = row.Id, Scope = row.Scope, Code = row.Code, Value = row.Value, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<SlotSeed>((row, saved) => new(row.Id, row.Scope, row.Code, row.Value, row.Version, row.Hidden,
                    saved == null ? "created" : saved.FixtureNote));
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection(row => new SlotView(row.Id, row.Code, row.Value, row.Version));
            entity.HasQuery("List").Where<SlotRequest>((row, request) => row.Scope == request.Scope)
                .OrderBy(row => row.Id).ProjectTo<SlotView>().List();

            entity.HasCommand("Ensure").Validate<SlotRequest>(request => request.Code != "", "A code is required.")
                .Create<SlotRequest>(request => new ReservationSlot
                    { Scope = request.Scope, Code = request.Code, Value = request.Value, Version = 1, Hidden = false })
                .AllowWrite(row => row.Version).InsertIfAbsent(row => new { row.Scope, row.Code })
                .Returns(row => new SlotView(row.Id, row.Code, row.Value, row.Version))
                .SimulateImmediateWrites(); // Explicit fixture-only saved-state insertion; no inferred defaults or races.
            entity.HasCommand("Put")
                .Create<SlotRequest>(request => new ReservationSlot
                    { Scope = request.Scope, Code = request.Code, Value = request.Value, Version = 1, Hidden = false })
                .InsertOrUpdate(row => new { row.Scope, row.Code }, setters => setters
                    .SetProperty<string, SlotRequest>(row => row.Value, (row, request) => request.Value)
                    .SetProperty(row => row.Version, row => row.Version + 1))
                .AllowWrite(row => row.Version)
                .ExpectVersion<int, SlotRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .Returns(row => new SlotView(row.Id, row.Code, row.Value, row.Version))
                .SimulateImmediateWrites(); // Visibility/version only guard existing-row updates, never insertion.
            entity.HasCommand("StageValue").Where<SlotRequest>((row, request) => row.Scope == request.Scope && row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, SlotRequest>(row => row.Value, (row, request) => request.Value)).Stage();
        });
    }
}
