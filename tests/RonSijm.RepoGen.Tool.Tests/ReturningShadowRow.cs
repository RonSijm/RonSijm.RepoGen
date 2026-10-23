namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowRow
{
    private int audit = 1;
    private readonly Dictionary<string, object?> extra = [];
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int Quantity { get; set; }
    public int Revision { get; set; } = 1;
    public int AuditValue() => audit;
    public object? this[string name] { get => extra.GetValueOrDefault(name); set => extra[name] = value; }
}
