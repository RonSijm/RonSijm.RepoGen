namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedKeys;

internal sealed class KeyedMemo
{
    public int Id { get; set; }
    public string Title { get; set; } = "entity-title";
    public KeyedFolder? Folder { get; set; }
}
