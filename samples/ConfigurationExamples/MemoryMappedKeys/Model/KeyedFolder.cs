namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedKeys;

internal sealed class KeyedFolder
{
    private string code = "entity-default";
    public int Id { get; set; }
    public string Name { get; set; } = "entity-name";
    public string Code => "display:" + code;
    public List<KeyedMemo> Memos { get; } = [];
}
