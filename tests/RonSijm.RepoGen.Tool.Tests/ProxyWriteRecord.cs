namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class ProxyWriteRecord
{
    internal static int Constructions;
    public ProxyWriteRecord() => Constructions++;
    public int Id { get; set; }
    public string Name { get; set; } = "constructor-default-is-not-a-fixture-policy";
    public int Version { get; set; }
    public string Label { get; set; } = null!;
    public bool Hidden { get; set; }
    public byte[] Stamp { get; set; } = [];
}
