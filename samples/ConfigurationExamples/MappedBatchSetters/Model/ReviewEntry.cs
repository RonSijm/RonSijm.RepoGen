namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedBatchSetters;

internal sealed class ReviewEntry
{
    private string caption = "initial";
#pragma warning disable CS0649 // EF's explicit field-only mapping assigns this value.
    private int edits;
#pragma warning restore CS0649
    private readonly Dictionary<string, object?> properties = [];
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string Caption => "display:" + caption;
    public int ReadEdits() => edits;
    public object? this[string name] { get => properties.GetValueOrDefault(name); set => properties[name] = value; }
}
