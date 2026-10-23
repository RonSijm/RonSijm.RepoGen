namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadItem
{
    private string caption = "initial";
    private int audit = 1;
    private bool archived = true;
    private MappedReadState? state = MappedReadState.Draft;
    private byte[]? fingerprint = [255];
    private readonly int fixedValue = 17;
    private readonly Dictionary<string, object?> values = [];

    public static AsyncLocal<int> GetterCalls { get; } = new();
    public int Id { get; set; }
    public string Name { get; set; } = "";
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Tests an explicitly mapped public EF field.")]
    public int PublicRank;
    public int? FolderId { get; set; }
    public MappedReadFolder? Folder { get; set; }
    public string Caption
    {
        get
        {
            GetterCalls.Value++;
            return "display:" + caption;
        }
    }
    public int ReadAudit() => audit;
    public bool IsArchived() => archived;
    public MappedReadState? ReadState() => state;
    public byte[]? ReadFingerprint() => fingerprint;
    public int FixedValue() => fixedValue;
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
