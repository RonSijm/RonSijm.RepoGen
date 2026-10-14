namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyParent
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<CreateManyRow> Rows { get; set; } = [];
}
