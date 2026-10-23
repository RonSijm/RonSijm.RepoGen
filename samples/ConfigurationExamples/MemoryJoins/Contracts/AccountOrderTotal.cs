namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;

public sealed record AccountOrderTotal(string AccountName, int Count, int Quantity)
{
    public AccountOrderTotal() : this("", 0, 0) { }
}
