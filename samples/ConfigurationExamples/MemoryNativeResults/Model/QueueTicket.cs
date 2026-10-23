namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeResults;

internal sealed class QueueTicket
{
    public int Id { get; set; }
    public int QueueId { get; set; }
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public int Revision { get; set; }
    public TicketQueue? Queue { get; set; }
}
