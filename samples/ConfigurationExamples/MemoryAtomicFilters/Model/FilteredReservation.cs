namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicFilters;

public sealed class FilteredReservation
{
    public int Id { get; set; }
    public string Scope { get; set; } = "";
    public string Code { get; set; } = "";
    public string Value { get; set; } = "";
    public int Version { get; set; }
    public bool Deleted { get; set; }
    public bool Hidden { get; set; }
}
