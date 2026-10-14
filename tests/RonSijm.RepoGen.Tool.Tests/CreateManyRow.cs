namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyRow
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Label { get; set; } = null!;
    public bool Hidden { get; set; }
    public CreateManyParent? Parent { get; set; }
}
