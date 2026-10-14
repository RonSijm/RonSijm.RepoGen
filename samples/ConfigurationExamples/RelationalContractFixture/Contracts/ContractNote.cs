namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ContractNotes;

public sealed class ContractNote
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Hidden { get; set; }
}
