namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicCapture;

internal sealed class CapturedReservation
{
    private string label = null!;
    private readonly Dictionary<string, object?> values = [];

    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int Counter { get; set; }
    public string Label => "display:" + label;
    public object? this[string name]
    {
        get => values.GetValueOrDefault(name);
        set => values[name] = value;
    }
}
