namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ContractNotes;

public sealed record ContractNoteSummary(int Id, string Name)
{
    public ContractNoteSummary() : this(0, "") { }
}
