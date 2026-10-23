namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyChild
{
    private readonly Dictionary<string, object?> values = [];
    public int Id { get; set; }
    public string Name { get; set; } = "entity-name";
    public bool Deleted { get; set; }
    public MappedKeyOwner? Owner { get; set; }
    public object? this[string name]
    {
        get
        {
            MappedKeyOwner.GetterCalls.Value++;
            return values.GetValueOrDefault(name);
        }
        set => values[name] = value;
    }
}
