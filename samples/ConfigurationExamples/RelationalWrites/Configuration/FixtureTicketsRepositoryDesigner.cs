using RonSijm.RepoGen.ConfigurationExamples.Contracts.FixtureTickets;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class FixtureTicketsRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.FixtureTickets")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.FixtureTickets")
            .UseAsyncContextLeases().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<FixtureTicket>(entity =>
        {
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id)
                .Select(row => new TicketSummary(row.Id, row.Title, row.Version)).SingleOrDefault();
            entity.HasCommand("Create").Create<CreateTicket>(request => new FixtureTicket { Title = request.Title, Version = 1 })
                .AllowWrite(row => row.Version).Save().Returns(row => new TicketSummary(row.Id, row.Title, row.Version));
            entity.HasCommand("StageRename").Where<RenameTicket>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, RenameTicket>(row => row.Title, (row, request) => request.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1))
                .AllowWrite(row => row.Version)
                .ExpectVersion<int, RenameTicket>(row => row.Version, (row, request) => request.ExpectedVersion).Stage();
        });
        modelBuilder.Entity<FixtureAudit>(entity =>
        {
            entity.ReadEnabled = false;
            entity.HasCommand("StageAudit").Create<AddAudit>(request => new FixtureAudit { TicketId = request.TicketId, Text = request.Text })
                .AllowWrite(row => row.TicketId).Stage();
        });
    }
}
