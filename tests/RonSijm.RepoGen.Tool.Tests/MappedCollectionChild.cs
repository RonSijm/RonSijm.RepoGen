namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionChild
{
    private string category = null!;
#pragma warning disable CS0649 // Assigned by the test's EF field-only mapping.
    private int audit;
#pragma warning restore CS0649
    private readonly Dictionary<string, object?> values = [];

    public static AsyncLocal<int> GetterCalls { get; } = new();
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public int Counter { get; set; }
    public string Category
    {
        get
        {
            GetterCalls.Value++;
            return "display:" + category;
        }
    }
    public int ReadAudit() => audit;
    public object? this[string name]
    {
        get
        {
            GetterCalls.Value++;
            return values.GetValueOrDefault(name);
        }
        set => values[name] = value;
    }
}
