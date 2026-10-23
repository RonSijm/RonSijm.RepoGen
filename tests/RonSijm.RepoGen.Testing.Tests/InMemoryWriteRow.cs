namespace RonSijm.RepoGen.Testing.Tests;

internal sealed class InMemoryWriteRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public string Unassigned { get; set; } = "";
    public static InMemoryWriteRow Copy(InMemoryWriteRow row) => new() { Id = row.Id, Name = row.Name, Version = row.Version, Unassigned = row.Unassigned };
}
