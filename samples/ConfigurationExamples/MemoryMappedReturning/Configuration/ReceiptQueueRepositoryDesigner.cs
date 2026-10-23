using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedReturning;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedReturning;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Native returned scalars reuse separately permitted fixture state, not entity getters or provider buffers.</summary>
public sealed class ReceiptQueueRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryMappedReturning")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryMappedReturning")
            .UseReturningWrites<SqliteReturningWriter>()
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryMappedReturning")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<ReceiptEnvelope>(entity =>
        {
            entity.BindReadPort<IDispatchReceiptReader>();
            entity.BindWritePort<IDispatchReceiptWriter>();
            entity.HasInMemoryStore<EnvelopeSeed, int>(row => row.Id, row => new(row.Id, row.Payload, row.Counter, row.Category, row.Revision))
                .MapInMemoryEntity<EnvelopeSeed>(row => new ReceiptEnvelope { Id = row.Id, Payload = row.Payload, Counter = row.Counter })
                .MapInMemoryProperty<EnvelopeSeed, string>(nameof(ReceiptEnvelope.Category), row => row.Category)
                .MapInMemoryProperty<EnvelopeSeed, int>("Revision", row => row.Revision)
                .MapInMemoryWrites<EnvelopeSeed>((row, saved) => new(row.Id, row.Payload, row.Counter, row.Category, EF.Property<int>(row, "Revision")),
                    useMappedPropertyState: true)
                .HasInMemoryGeneratedValue<string>(nameof(ReceiptEnvelope.Category), (row, rows) => "notification")
                // A deliberate fixture recipe matching this example's computed column, never inferred by RepoGen.
                .HasInMemoryGeneratedValue<int>("Revision", (row, rows) => row.Counter + 1);
            entity.HasQuery("List").OrderBy(row => row.Id)
                .Select(row => new DispatchReceipt(row.Id, row.Payload, row.Category, EF.Property<int>(row, "Revision"))).List();
            entity.HasCommand("Advance").Where<ReceiptRequest>((row, request) => row.Id == request.Id)
                .ExpectVersion<int, ReceiptRequest>("Revision", request => request.ExpectedRevision)
                .Update(setters => setters.SetProperty(row => row.Counter, row => row.Counter + 1))
                .ReturnsMany(row => new DispatchReceipt(row.Id, row.Payload, row.Category, EF.Property<int>(row, "Revision")))
                .IncludeMappedProperties().SimulateImmediateWrites();
            entity.HasCommand("Consume").Where<ReceiptRequest>((row, request) => row.Id == request.Id)
                .ExpectVersion<int, ReceiptRequest>("Revision", request => request.ExpectedRevision).Delete()
                .ReturnsMany(row => new DispatchReceipt(row.Id, row.Payload, row.Category, EF.Property<int>(row, "Revision")))
                .IncludeMappedProperties().SimulateImmediateWrites();
        });
    }
}
