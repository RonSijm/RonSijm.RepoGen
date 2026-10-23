using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeResults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeResults;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>Bounded fresh DTO graphs after a native update; no shared Save or consumer query DSL.</summary>
public sealed class QueueResultsRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Commands = ["Relabel", "RelabelAtMostOne"];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryNativeResults")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryNativeResults")
            .UseReturningWrites<SqliteReturningWriter>()
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryNativeResults")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<TicketQueue>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<ITicketQueueReader>();
            entity.HasInMemoryStore<TicketQueueSeed, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<TicketQueueSeed>(row => new TicketQueue { Id = row.Id, Name = row.Name });
            entity.HasInMemoryNavigation(row => row.Tickets);
            entity.HasQuery("Name").Where<int>((row, queueId) => row.Id == queueId).Select(row => row.Name).Single();
        });
        modelBuilder.Entity<QueueTicket>(entity =>
        {
            entity.ReadEnabled = false;
            entity.BindWritePort<IQueueTicketWriter>();
            entity.HasInMemoryStore<QueueTicketSeed, int>(row => row.Id, row => new(row.Id, row.QueueId, row.Code, row.Label, row.Revision))
                .MapInMemoryEntity<QueueTicketSeed>(row => new QueueTicket
                    { Id = row.Id, QueueId = row.QueueId, Code = row.Code, Label = row.Label, Revision = row.Revision })
                .MapInMemoryWrites<QueueTicketSeed>((row, saved) => new(row.Id, row.QueueId, row.Code, row.Label, row.Revision));
            entity.HasInMemoryNavigation(row => row.Queue);
            foreach (var name in Commands)
            {
                entity.HasCommand(name).Where<QueueRelabelRequest>((row, request) => row.QueueId == request.QueueId)
                    .ExpectVersion<int, QueueRelabelRequest>(row => row.Revision, (row, request) => request.ExpectedRevision)
                    .Update(setters => setters.SetProperty<string, QueueRelabelRequest>(row => row.Label, (row, request) => request.Prefix + row.Code)
                        .SetProperty(row => row.Revision, row => row.Revision + 1))
                    .AllowWrite(row => row.Revision)
                    .ReturnsSavedMany(row => new QueueTicketReceipt(row.Id, row.Label, row.Revision,
                        row.Queue == null ? "missing" : row.Queue.Name,
                        row.Queue == null ? new List<string>() : row.Queue.Tickets.OrderBy(ticket => ticket.Code).Select(ticket => ticket.Label).ToList()),
                        maximumRows: name == "RelabelAtMostOne" ? 1 : 20)
                    .SimulateImmediateWrites();
            }
        });
    }
}
