namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedStateNote
{
    private string caption = null!;
#pragma warning disable CS0649 // The test's EF field-only mapping assigns this field.
    private int audit;
#pragma warning restore CS0649
    private readonly Dictionary<string, object?> values = [];

    public static AsyncLocal<int> GetterCalls { get; } = new();
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Counter { get; set; }
    public string Caption
    {
        get
        {
            GetterCalls.Value++;
            return "display:" + caption;
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
