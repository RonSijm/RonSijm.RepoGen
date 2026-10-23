namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;

public sealed class OrderEntry
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string Region { get; set; } = "";
    public int Quantity { get; set; }
    public bool Hidden { get; set; }
}
