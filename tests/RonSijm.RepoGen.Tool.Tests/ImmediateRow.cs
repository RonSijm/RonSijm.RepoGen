namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ImmediateRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Code { get; set; } = "";
    public int Amount { get; set; }
    public int Previous { get; set; }
    public bool Hidden { get; set; }
    public int Version { get; set; }
    public byte[] Stamp { get; set; } = [];
}
