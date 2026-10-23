namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedSetters;

internal sealed class EditableNote
{
    private string caption = "initial";
    private readonly Dictionary<string, object?> properties = [];
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Caption => "getter behavior:" + caption;
    public object? this[string name] { get => properties.GetValueOrDefault(name); set => properties[name] = value; }
}
