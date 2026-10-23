namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedSetterNote
{
    private string caption = "initial";
    private int audit = 1;
    private readonly Dictionary<string, object?> values = [];
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? OwnerId { get; set; }
    public string UniqueCode { get; set; } = "";
    public string Caption => "getter behavior:" + caption;
    public int AuditValue() => audit;
    public object? this[string name] { get => values.GetValueOrDefault(name); set => values[name] = value; }
}
