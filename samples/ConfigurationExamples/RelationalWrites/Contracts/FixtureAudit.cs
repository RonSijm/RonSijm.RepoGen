namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.FixtureTickets;

public sealed class FixtureAudit
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public string Text { get; set; } = "";
}
