namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyOwner
{
    internal static readonly AsyncLocal<int> GetterCalls = new();
    private string code = "entity-default";
    public int Id { get; set; }
    public string Name { get; set; } = "entity-name";
    public string Code
    {
        get
        {
            GetterCalls.Value++;
            return "display:" + code;
        }
    }
    public List<MappedKeyChild> Children { get; } = [];
    public List<MappedKeyTag> Tags { get; } = [];
}
