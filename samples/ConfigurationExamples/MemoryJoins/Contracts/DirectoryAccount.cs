namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;

public sealed class DirectoryAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public bool Hidden { get; set; }
}
