namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedTextIndexerRow
{
    private readonly Dictionary<string, string> values = [];
    public int Id { get; set; }
    public string this[string name]
    {
        get => values.GetValueOrDefault(name, "entity getter");
        set => values[name] = value;
    }
}
