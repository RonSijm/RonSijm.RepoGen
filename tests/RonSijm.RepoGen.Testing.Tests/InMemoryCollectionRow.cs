namespace RonSijm.RepoGen.Testing.Tests;

internal sealed class InMemoryCollectionRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public static InMemoryCollectionRow Copy(InMemoryCollectionRow row)
        => new() { Id = row.Id, OwnerId = row.OwnerId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden };
}
