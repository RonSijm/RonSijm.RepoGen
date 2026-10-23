namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyTag
{
    public int Id { get; set; }
    public string Name { get; set; } = "entity-name";
    public List<MappedKeyOwner> Owners { get; } = [];
}
