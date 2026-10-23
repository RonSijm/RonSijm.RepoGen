namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyLink
{
    private string? ownerCode = "entity-default";
    private readonly Dictionary<string, object?> values = [];
    public int Id { get; set; }
    public string? ReadOwnerCode() => ownerCode;
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
