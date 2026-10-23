namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedReturning;

internal sealed class ReceiptEnvelope
{
    private string category = null!;

    public int Id { get; set; }
    public string Payload { get; set; } = "";
    public int Counter { get; set; }
    public string Category => "display:" + category;
}
