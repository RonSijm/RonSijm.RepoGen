namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNotebook;

public readonly record struct NoteKey(int Value);
public sealed record MemoryNoteSummary(int Id, string Name);

// These plain data shapes have no EF dependency. The design-time context maps them in the Models project.
public sealed class MemoryNote
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<string> Labels { get; set; } = [];
}

public sealed class MemoryLabel
{
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
}

public interface IMemoryNoteLookup
{
    Task<MemoryNoteSummary?> FindAsync(int id, CancellationToken cancellationToken = default);
}
