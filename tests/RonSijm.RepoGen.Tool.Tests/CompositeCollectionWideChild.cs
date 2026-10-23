namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionWideChild
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int Key1 { get; set; }
    public int Key2 { get; set; }
    public int Key3 { get; set; }
    public int Key4 { get; set; }
    public int Key5 { get; set; }
    public int Key6 { get; set; }
    public int Key7 { get; set; }
    public (string, string) PackedKey { get; set; }
    public (string, string)? NullablePackedKey { get; set; }
}
