namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;

public sealed record OrderAccountRow(int OrderId, string? AccountName, int Quantity)
{
    public OrderAccountRow() : this(0, null, 0) { }
}
