namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedCollectionSetters;

internal sealed class ReviewItem
{
    private string caption = "initial";
#pragma warning disable CS0649 // EF's explicit field-only mapping assigns this value.
    private int edits;
#pragma warning restore CS0649
    private readonly Dictionary<string, object?> properties = [];
    public int Id { get; set; }
    public int BoardId { get; set; }
    public string Code { get; set; } = "";
    public string Caption => "display:" + caption;
    public int ReadEdits() => edits;
    public object? this[string name] { get => properties.GetValueOrDefault(name); set => properties[name] = value; }
}
