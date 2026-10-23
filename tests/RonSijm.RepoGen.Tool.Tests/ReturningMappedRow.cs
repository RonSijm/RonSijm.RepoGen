namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedRow
{
    private string caption = "initial";
    private int audit = 1;
    // Non-default constructor state must be replaced even when the database returns null.
    private ReturningState? state = ReturningState.Pending;
    private byte[]? fingerprint = [255];
    private readonly int immutable = 1;
    private readonly Dictionary<string, object?> values = [];

    public static AsyncLocal<bool> FailIndexerAssignment { get; } = new();
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string Caption => "getter behavior:" + caption;
    public int AuditValue() => audit;
    public int ImmutableValue() => immutable;
    public ReturningState? StateValue() => state;
    public byte[]? FingerprintValue() => fingerprint;
    public object? this[string name]
    {
        get => values.GetValueOrDefault(name);
        set
        {
            if (FailIndexerAssignment.Value) throw new InvalidOperationException("Indexer materialization rejected.");
            values[name] = value;
        }
    }
}
