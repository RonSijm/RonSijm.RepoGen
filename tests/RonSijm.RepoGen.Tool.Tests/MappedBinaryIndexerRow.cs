namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedBinaryIndexerRow
{
    private readonly Dictionary<string, byte[]> values = [];
    public int Id { get; set; }
    public byte[] this[string name]
    {
        get => values.GetValueOrDefault(name, [255]);
        set => values[name] = value;
    }
}
