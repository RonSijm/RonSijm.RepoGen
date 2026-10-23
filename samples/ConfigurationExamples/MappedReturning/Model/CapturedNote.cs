namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedReturning;

public sealed class CapturedNote
{
    private string normalizedTitle = "";
    private int version = 1;
    private readonly Dictionary<string, object?> values = [];

    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ChangeCounter { get; set; }
    // Native mapped results use the persisted scalar, not this application getter behavior.
    public string NormalizedTitle => "display:" + normalizedTitle;
    public int ReadVersion() => version;
    public object? this[string name] { get => values.GetValueOrDefault(name); set => values[name] = value; }
}
