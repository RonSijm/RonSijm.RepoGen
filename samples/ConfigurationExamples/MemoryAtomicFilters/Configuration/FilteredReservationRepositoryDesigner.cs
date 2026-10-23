using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicFilters;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Unfiltered insert/conflict branches, filtered updates and model-only updated result reads.</summary>
public sealed class FilteredReservationRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] PutNames = ["Put", "ArchivePut"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryAtomicFilters")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryAtomicFilters")
            .UseAtomicWrites<SqliteAtomicWriter>() // Production capability; the fixture consumer does not reference it.
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicFilters")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<FilteredReservation>(entity =>
        {
            entity.BindReadPort<IFilteredSlotReader>(); entity.BindWritePort<IFilteredSlotWriter>();
            entity.HasInMemoryStore<FilteredSlotSeed, int>(row => row.Id, row => new(row.Id, row.Scope, row.Code, row.Value, row.Version, row.Deleted, row.Hidden))
                .MapInMemoryEntity<FilteredSlotSeed>(row => new FilteredReservation
                    { Id = row.Id, Scope = row.Scope, Code = row.Code, Value = row.Value, Version = row.Version, Deleted = row.Deleted, Hidden = row.Hidden })
                .MapInMemoryWrites<FilteredSlotSeed>((row, saved) => new(row.Id, row.Scope, row.Code, row.Value, row.Version, row.Deleted, row.Hidden));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.UseInMemoryModelQueryFilter(); // Read permission never enables any command below.
            entity.HasQuery("List").OrderBy(row => row.Id).Select(row => new FilteredSlotView(row.Id, row.Code, row.Value, row.Version, row.Deleted)).List();
            Configure(entity.HasCommand("Ensure").Create<FilteredSlotInput>(input => new FilteredReservation
                { Id = input.Id, Scope = input.Scope, Code = input.Code, Value = input.Value, Version = 0, Deleted = input.Deleted, Hidden = input.Hidden })
                .InsertIfAbsent(row => new { row.Scope, row.Code }));
            foreach (var name in PutNames)
            {
                Configure(entity.HasCommand(name).Create<FilteredSlotInput>(input => new FilteredReservation
                    { Id = input.Id, Scope = input.Scope, Code = input.Code, Value = input.Value, Version = 0, Deleted = input.Deleted, Hidden = input.Hidden })
                    .InsertOrUpdate(row => new { row.Scope, row.Code }, setters =>
                    {
                        setters.SetProperty<string, FilteredSlotInput>(row => row.Value, (row, input) => input.Value)
                            .SetProperty(row => row.Version, row => row.Version + 1);
                        if (name == "ArchivePut") setters.SetProperty(row => row.Deleted, true);
                    }).ExpectVersion<int, FilteredSlotInput>(row => row.Version, (row, input) => input.ExpectedVersion));
            }
            entity.HasCommand("StageValue").Where<FilteredSlotInput>((row, input) => row.Id == input.Id)
                .Patch(setters => setters.SetProperty<string, FilteredSlotInput>(row => row.Value, (row, input) => input.Value))
                .ExpectVersion<int, FilteredSlotInput>(row => row.Version, (row, input) => input.ExpectedVersion).UseInMemoryModelQueryFilter().Stage();
        });
    }

    private static void Configure(RepositoryCommandBuilder<FilteredReservation> command) => command.AllowWrite(row => row.Id).AllowWrite(row => row.Version)
        .Returns(row => new FilteredSlotView(row.Id, row.Code, row.Value, row.Version, row.Deleted))
        .SimulateImmediateWrites().UseInMemoryModelQueryFilter(); // Separate explicit permissions; neither enables graph mutations.
}
