namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedReads;

internal sealed class StoredMappedMemo
{
    private string caption = "entity default";
    private int revision = 17;
    private bool deleted = true;
    private readonly Dictionary<string, object?> values = [];

    public int Id { get; set; }
    public string Caption => "display:" + caption;
    public int ReadRevision() => revision;
    public bool IsDeleted() => deleted;
    public object? this[string name] { get => values.GetValueOrDefault(name); set => values[name] = value; }
}
