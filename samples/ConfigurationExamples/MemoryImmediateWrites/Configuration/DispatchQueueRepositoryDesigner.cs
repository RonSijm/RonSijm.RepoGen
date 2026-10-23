// DispatchQueueContext -> Contracts/Generated/DispatchQueueRepositories.g.cs + this example's Generated/TestDoubles.g.cs.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryImmediateWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryImmediateWrites;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class DispatchQueueRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryImmediateWrites")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryImmediateWrites")
            .UseReturningWrites<SqliteReturningWriter>() // Production capability; NOT referenced by the fake consumer.
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryImmediateWrites")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<QueuedEnvelope>(entity =>
        {
            entity.HasInMemoryStore<EnvelopeSeed, int>(row => row.Id,
                    row => new(row.Id, row.Queue, row.Body, row.Attempts, row.PreviousAttempts, row.Version, row.Hidden))
                .MapInMemoryEntity<EnvelopeSeed>(row => new QueuedEnvelope
                    { Id = row.Id, Queue = row.Queue, Body = row.Body, Attempts = row.Attempts, PreviousAttempts = row.PreviousAttempts, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<EnvelopeSeed>((row, saved) => new(row.Id, row.Queue, row.Body, row.Attempts, row.PreviousAttempts, row.Version, row.Hidden));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection(row => new EnvelopeView(row.Id, row.Body, row.Attempts, row.PreviousAttempts, row.Version));
            entity.HasQuery("List").Where<EnvelopeRequest>((row, request) => row.Queue == request.Queue)
                .OrderBy(row => row.Id).ProjectTo<EnvelopeView>().List();

            entity.HasCommand("Requeue").Where<EnvelopeRequest>((row, request) => row.Queue == request.Queue)
                .Update(setters => setters.SetProperty(row => row.Attempts, row => row.Attempts + 1)
                    .SetProperty(row => row.PreviousAttempts, row => row.Attempts)
                    .SetProperty(row => row.Version, row => row.Version + 1))
                .AllowWrite(row => row.Version)
                .ReturnsMany(row => new EnvelopeView(row.Id, row.Body, row.Attempts, row.PreviousAttempts, row.Version))
                .SimulateImmediateWrites(); // Explicit TEST choice. All setters read the original saved row.
            entity.HasCommand("Consume").Where<EnvelopeRequest>((row, request) => row.Queue == request.Queue && row.Id == request.Id)
                .ExpectVersion<int, EnvelopeRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .Delete().ReturnsMany(row => new EnvelopeView(row.Id, row.Body, row.Attempts, row.PreviousAttempts, row.Version))
                .SimulateImmediateWrites(); // Empty on missing/stale/hidden rows; no production locking guarantee.
            entity.HasCommand("ClearQueue").Where<EnvelopeRequest>((row, request) => row.Queue == request.Queue)
                .Delete().WithOutcome().SimulateImmediateWrites();
            entity.HasCommand("MarkAttempt").Where<EnvelopeRequest>((row, request) => row.Queue == request.Queue)
                .Update(setters => setters.SetProperty(row => row.Attempts, row => row.Attempts + 1))
                .SimulateImmediateWrites(); // Count-only Task<int> variant; also executes immediately.
            entity.HasCommand("StageBody").Where<EnvelopeRequest>((row, request) => row.Queue == request.Queue && row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, EnvelopeRequest>(row => row.Body, (row, request) => request.Body)).Stage();
        });
    }
}
