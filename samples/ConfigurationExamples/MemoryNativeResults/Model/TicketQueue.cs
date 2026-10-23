namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeResults;

internal sealed class TicketQueue
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<QueueTicket> Tickets { get; set; } = new List<QueueTicket>();
}
