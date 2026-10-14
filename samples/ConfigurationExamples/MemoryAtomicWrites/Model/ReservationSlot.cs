namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicWrites;

internal sealed class ReservationSlot
{
    public int Id { get; set; }
    public string Scope { get; set; } = "";
    public string Code { get; set; } = "";
    public string Value { get; set; } = "";
    public int Version { get; set; }
    public bool Hidden { get; set; }
}
