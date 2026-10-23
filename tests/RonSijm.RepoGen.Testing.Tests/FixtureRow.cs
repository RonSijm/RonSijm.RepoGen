namespace RonSijm.RepoGen.Testing.Tests;

public sealed class FixtureRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<string> Tags { get; set; } = [];

    internal static FixtureRow Copy(FixtureRow row) => new() { Id = row.Id, Name = row.Name, Tags = row.Tags.ToList() };
}
