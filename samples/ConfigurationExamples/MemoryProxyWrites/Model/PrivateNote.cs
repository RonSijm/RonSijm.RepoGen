namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProxyWrites;

// Not visible to the EF-free example executable. The fixture must never instantiate this class.
internal sealed class PrivateNote
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Version { get; set; }
    public bool Hidden { get; set; }
    public byte[] Stamp { get; set; } = [];
}
