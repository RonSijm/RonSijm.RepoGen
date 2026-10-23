using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicCapture;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicCapture;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Native atomic scalar capture and independently permitted public-data fixture semantics.</summary>
public sealed class CapturedReservationsRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Commands = ["Ensure", "Put"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryAtomicCapture")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryAtomicCapture")
            .UseAtomicWrites<SqliteAtomicWriter>()
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicCapture")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<CapturedReservation>(entity =>
        {
            entity.ReadEnabled = false;
            entity.BindWritePort<ICapturedReservationWriter>();
            entity.HasInMemoryStore<CapturedReservationSeed, int>(row => row.Id,
                    row => new(row.Id, row.Code, row.Counter, row.Label, row.Revision, row.Route))
                .MapInMemoryEntity<CapturedReservationSeed>(row => new CapturedReservation { Id = row.Id, Code = row.Code, Counter = row.Counter })
                .MapInMemoryProperty<CapturedReservationSeed, string>(nameof(CapturedReservation.Label), row => row.Label)
                .MapInMemoryProperty<CapturedReservationSeed, int>("Revision", row => row.Revision)
                .MapInMemoryProperty<CapturedReservationSeed, string?>("Route", row => row.Route)
                .MapInMemoryWrites<CapturedReservationSeed>((row, saved) => new(row.Id, row.Code, row.Counter, row.Label,
                    EF.Property<int>(row, "Revision"), EF.Property<string?>(row, "Route")), useMappedPropertyState: true)
                // Explicit fixture recipes match only this example's selected SQLite expressions/defaults.
                .HasInMemoryGeneratedValue<string>(nameof(CapturedReservation.Label), (row, rows) => row.Code.ToUpperInvariant())
                .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1)
                .HasInMemoryGeneratedValue<string?>("Route", (row, rows) => "queued");
            foreach (var name in Commands)
            {
                var command = entity.HasCommand(name)
                    .Create<ReservationChange>(request => new CapturedReservation { Id = request.Id, Code = request.Code, Counter = request.Delta })
                    .AllowWrite(row => row.Id);
                if (name == "Ensure")
                {
                    command.InsertIfAbsent(row => row.Code);
                }
                else
                {
                    command.InsertOrUpdate(row => row.Code, setters => setters
                            .SetProperty<int, ReservationChange>(row => row.Counter, (row, request) => row.Counter + request.Delta))
                        .ExpectVersion<int, ReservationChange>("Revision", request => request.ExpectedRevision);
                }
                command.Returns(row => new ReservationReceipt(row.Id, row.Code, row.Counter, row.Label,
                        EF.Property<int>(row, "Revision"), EF.Property<string?>(row, "Route")))
                    .IncludeMappedProperties().SimulateImmediateWrites();
            }
        });
    }
}
